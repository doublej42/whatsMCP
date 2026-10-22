using System.Diagnostics;
using System.Text;
using WhatsBuildingMcp.Models;
using WhatsBuildingMcp.Options;

namespace WhatsBuildingMcp.Services;

/// <summary>
/// Typed HTTP client for the public What's Building endpoints. Anonymous GETs only
/// (plus one anonymous search-form POST for date-window markers); never touches
/// <c>Identity/*</c> routes or any host other than the configured base URL.
/// </summary>
public sealed class WhatsBuildingClient
{
    private const string NotFoundMarker = "The application was not found";

    private readonly HttpClient _http;
    private readonly WhatsBuildingOptions _options;
    private readonly ILogger<WhatsBuildingClient> _logger;
    private readonly Uri _baseUri;

    public WhatsBuildingClient(HttpClient http, IOptions<WhatsBuildingOptions> options, ILogger<WhatsBuildingClient> logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _http = http;
        _options = options.Value;
        _logger = logger;
        _baseUri = new Uri(_options.BaseUrl, UriKind.Absolute);
    }

    /// <summary>Configured upstream base URI (used to resolve relative links).</summary>
    public Uri BaseUri => _baseUri;

    public Task<string> GetCsvAsync(CancellationToken cancellationToken)
        => GetStringAsync("whatsbuilding.csv", _options.MaxCsvBytes, cancellationToken);

    /// <summary>Returns null when the file number is unknown or not public.</summary>
    public async Task<string?> GetFolderViewAsync(string fileNumber, CancellationToken cancellationToken)
    {
        RequireValidFileNumber(fileNumber);
        var body = await GetOptionalStringAsync(
            "FolderView/" + Uri.EscapeDataString(fileNumber),
            _options.MaxResponseBytes,
            cancellationToken).ConfigureAwait(false);
        if (body is not null && body.Contains(NotFoundMarker, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return body;
    }

    /// <summary>Returns null when no KML exists for the file number.</summary>
    public Task<string?> GetKmlAsync(string fileNumber, CancellationToken cancellationToken)
    {
        RequireValidFileNumber(fileNumber);
        return GetOptionalStringAsync(
            Uri.EscapeDataString(fileNumber) + ".KML",
            _options.MaxResponseBytes,
            cancellationToken);
    }

    public Task<string> GetPendingPermitsHtmlAsync(CancellationToken cancellationToken)
        => GetStringAsync("PendingPermits", _options.MaxResponseBytes, cancellationToken);

    public Task<string> GetAboutHtmlAsync(CancellationToken cancellationToken)
        => GetStringAsync("about", _options.MaxResponseBytes, cancellationToken);

    public Task<string> GetHomeHtmlAsync(CancellationToken cancellationToken)
        => GetStringAsync(string.Empty, _options.MaxResponseBytes, cancellationToken);

    /// <summary>
    /// Posts the complete scraped search form to <c>/GetMarkers</c> with a status-change
    /// window applied, so inactive/completed applications in the window are included.
    /// </summary>
    public async Task<string> PostMarkersAsync(
        IReadOnlyList<FormField> formDefaults,
        string newStatus,
        DateOnly windowStart,
        DateOnly windowEnd,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(formDefaults);
        ArgumentException.ThrowIfNullOrWhiteSpace(newStatus);

        var pairs = new List<KeyValuePair<string, string?>>(formDefaults.Count + 2);
        foreach (var field in formDefaults)
        {
            // Drop fields we override below; the rest ride along untouched.
            if (field.Name.Equals("SearchCriteria.NewStatus", StringComparison.Ordinal)
                || field.Name.Equals("SearchCriteria.StatusStartDate", StringComparison.Ordinal)
                || field.Name.Equals("SearchCriteria.StatusEndDate", StringComparison.Ordinal))
            {
                continue;
            }

            pairs.Add(new KeyValuePair<string, string?>(field.Name, field.Value));
        }

        pairs.Add(new KeyValuePair<string, string?>("SearchCriteria.NewStatus", newStatus));
        pairs.Add(new KeyValuePair<string, string?>("SearchCriteria.StatusStartDate", windowStart.ToString("yyyy-MM-dd", DateUtil.EnUs)));
        pairs.Add(new KeyValuePair<string, string?>("SearchCriteria.StatusEndDate", windowEnd.ToString("yyyy-MM-dd", DateUtil.EnUs)));

        // MVC check boxes post "true" then a hidden "false"; the binder takes the first
        // value, so checking the box means prepending "true" ahead of the hidden "false".
        pairs.Insert(0, new KeyValuePair<string, string?>("SearchCriteria.ShowInactiveFolders", "true"));

        using var content = new FormUrlEncodedContent(pairs);
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("GetMarkers")) { Content = content };
        var stopwatch = Stopwatch.StartNew();
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new UpstreamException($"Upstream POST GetMarkers failed with {(int)response.StatusCode}.");
        }

        var body = await ReadCappedStringAsync(response, _options.MaxResponseBytes, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Upstream POST GetMarkers returned {Bytes} bytes in {ElapsedMs} ms", body.Length, stopwatch.ElapsedMilliseconds);
        return body;
    }

    private static void RequireValidFileNumber(string fileNumber)
    {
        if (!InputValidation.IsValidFileNumber(fileNumber))
        {
            throw new ArgumentException($"Invalid file number '{fileNumber}'.", nameof(fileNumber));
        }
    }

    private Uri BuildUri(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        if (relativePath.Contains("://", StringComparison.Ordinal)
            || relativePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            throw new UpstreamException("Absolute upstream URLs are not allowed.");
        }

        var uri = new Uri(_baseUri, relativePath);
        if (!uri.Host.Equals(_baseUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            throw new UpstreamException("Blocked upstream URL escaping the configured host.");
        }

        if (uri.AbsolutePath.Contains("identity", StringComparison.OrdinalIgnoreCase))
        {
            throw new UpstreamException("Blocked upstream Identity route.");
        }

        return uri;
    }

    private async Task<string> GetStringAsync(string relativePath, long maxBytes, CancellationToken cancellationToken)
    {
        var body = await GetOptionalStringAsync(relativePath, maxBytes, cancellationToken).ConfigureAwait(false);
        return body ?? throw new UpstreamException($"Upstream endpoint '{relativePath}' returned 404.");
    }

    private async Task<string?> GetOptionalStringAsync(string relativePath, long maxBytes, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(relativePath));
        var stopwatch = Stopwatch.StartNew();
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new UpstreamException($"Upstream endpoint '{relativePath}' failed with {(int)response.StatusCode}.");
        }

        var body = await ReadCappedStringAsync(response, maxBytes, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Upstream GET {Path} returned {Bytes} bytes in {ElapsedMs} ms", relativePath, body.Length, stopwatch.ElapsedMilliseconds);
        return body;
    }

    private static async Task<string> ReadCappedStringAsync(HttpResponseMessage response, long maxBytes, CancellationToken cancellationToken)
    {
        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength.HasValue && contentLength.Value > maxBytes)
        {
            throw new UpstreamException($"Upstream response of {contentLength.Value} bytes exceeds the {maxBytes} byte limit.");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var remaining = maxBytes + 1;
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            remaining -= read;
            if (remaining < 0)
            {
                throw new UpstreamException($"Upstream response exceeds the {maxBytes} byte limit.");
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
