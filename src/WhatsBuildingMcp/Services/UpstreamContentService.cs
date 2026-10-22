using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using WhatsBuildingMcp.Models;
using WhatsBuildingMcp.Options;

namespace WhatsBuildingMcp.Services;

/// <summary>Cached access to the auxiliary upstream pages (pending permits, about, KML, marker windows).</summary>
public interface IUpstreamContentService
{
    Task<PendingPermitsResult> GetPendingPermitsAsync(CancellationToken cancellationToken = default);

    Task<AboutResult> GetAboutTextAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns a null KML when none exists for the file number.</summary>
    Task<KmlResult> GetKmlAsync(string fileNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Advanced path: posts the full search form for a status-change window.
    /// Returns an empty list plus a warning when unavailable.
    /// </summary>
    Task<MarkersResult> GetRecentMarkersAsync(string newStatus, DateOnly windowStart, DateOnly windowEnd, CancellationToken cancellationToken = default);
}

/// <summary>Cached about-page text.</summary>
public sealed record AboutResult(string Text, DateTimeOffset FetchedUtc, string? Warning = null);

/// <summary>Fetched KML document (null when unavailable).</summary>
public sealed record KmlResult(string? Kml, string KmlUrl, string? Warning = null);

/// <summary>Upstream-window markers (empty with a warning when the advanced path fails).</summary>
public sealed record MarkersResult(IReadOnlyList<Marker> Markers, bool Truncated, string? Warning = null);

/// <summary>Short-TTL cached fetches with stale-on-failure and request de-duplication.</summary>
public sealed class UpstreamContentService : IUpstreamContentService, IDisposable
{
    private const int MaxMarkers = 500;
    private static readonly JsonSerializerOptions MarkerJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IMemoryCache _cache;
    private readonly WhatsBuildingClient _client;
    private readonly IReferenceDataService _referenceData;
    private readonly WhatsBuildingOptions _options;
    private readonly ILogger<UpstreamContentService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    public UpstreamContentService(
        IMemoryCache cache,
        WhatsBuildingClient client,
        IReferenceDataService referenceData,
        IOptions<WhatsBuildingOptions> options,
        ILogger<UpstreamContentService> logger)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(referenceData);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _cache = cache;
        _client = client;
        _referenceData = referenceData;
        _options = options.Value;
        _logger = logger;
    }

    public Task<PendingPermitsResult> GetPendingPermitsAsync(CancellationToken cancellationToken = default)
        => GetCachedAsync(
            "whatsbuilding:pending",
            TimeSpan.FromMinutes(_options.DetailCacheMinutes),
            async ct =>
            {
                var html = await _client.GetPendingPermitsHtmlAsync(ct).ConfigureAwait(false);
                return new PendingPermitsResult(PendingPermitsParser.Parse(html), DateTimeOffset.UtcNow);
            },
            result => result with { Warning = "Serving stale pending-permit data; the upstream refresh failed." },
            new PendingPermitsResult([], DateTimeOffset.UtcNow, "Pending-permit data is unavailable."),
            cancellationToken);

    public Task<AboutResult> GetAboutTextAsync(CancellationToken cancellationToken = default)
        => GetCachedAsync(
            "whatsbuilding:about",
            TimeSpan.FromHours(_options.ReferenceDataCacheHours),
            async ct =>
            {
                var html = await _client.GetAboutHtmlAsync(ct).ConfigureAwait(false);
                return new AboutResult(AboutParser.ExtractText(html), DateTimeOffset.UtcNow);
            },
            result => result with { Warning = "Serving stale about text; the upstream refresh failed." },
            new AboutResult("About text is unavailable.", DateTimeOffset.UtcNow, "About text is unavailable."),
            cancellationToken);

    public async Task<KmlResult> GetKmlAsync(string fileNumber, CancellationToken cancellationToken = default)
    {
        if (!InputValidation.IsValidFileNumber(fileNumber))
        {
            throw new ArgumentException($"Invalid file number '{fileNumber}'.", nameof(fileNumber));
        }

        var kmlUrl = _client.BaseUri.AbsoluteUri.TrimEnd('/') + "/" + Uri.EscapeDataString(fileNumber) + ".KML";
        var key = "whatsbuilding:kml:" + fileNumber.ToUpperInvariant();
        if (_cache.TryGetValue(key, out KmlResult? cached) && cached is not null)
        {
            return cached;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cache.TryGetValue(key, out cached) && cached is not null)
            {
                return cached;
            }

            KmlResult result;
            try
            {
                var kml = await _client.GetKmlAsync(fileNumber, cancellationToken).ConfigureAwait(false);
                result = kml is null
                    ? new KmlResult(null, kmlUrl, $"No KML is published for '{fileNumber}'.")
                    : new KmlResult(kml, kmlUrl);
            }
#pragma warning disable CA1031 // KML fetch must degrade to a warning, never throw.
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "KML fetch failed for {FileNumber}", fileNumber);
                result = new KmlResult(null, kmlUrl, "KML data is unavailable.");
            }
#pragma warning restore CA1031

            _cache.Set(key, result, TimeSpan.FromMinutes(_options.DetailCacheMinutes));
            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<MarkersResult> GetRecentMarkersAsync(
        string newStatus,
        DateOnly windowStart,
        DateOnly windowEnd,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newStatus);
        var key = $"whatsbuilding:markers:{newStatus}:{windowStart:yyyy-MM-dd}:{windowEnd:yyyy-MM-dd}";
        if (_cache.TryGetValue(key, out MarkersResult? cached) && cached is not null)
        {
            return cached;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cache.TryGetValue(key, out cached) && cached is not null)
            {
                return cached;
            }

            var result = await LoadMarkersAsync(newStatus, windowStart, windowEnd, cancellationToken).ConfigureAwait(false);
            _cache.Set(key, result, TimeSpan.FromMinutes(_options.RecentActivityCacheMinutes));
            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _lock.Dispose();
            _disposed = true;
        }
    }

    private async Task<MarkersResult> LoadMarkersAsync(string newStatus, DateOnly windowStart, DateOnly windowEnd, CancellationToken cancellationToken)
    {
        try
        {
            var reference = await _referenceData.GetReferenceDataAsync(cancellationToken).ConfigureAwait(false);
            if (!reference.IsLive || reference.FormDefaults.Count == 0)
            {
                return new MarkersResult([], false, "Upstream date-window search is not available; showing cached snapshot activity only.");
            }

            var json = await _client.PostMarkersAsync(reference.FormDefaults, newStatus, windowStart, windowEnd, cancellationToken).ConfigureAwait(false);
            var markers = JsonSerializer.Deserialize<List<Marker>>(json, MarkerJson) ?? [];
            var truncated = markers.Count > MaxMarkers;
            return new MarkersResult(
                truncated ? markers.Take(MaxMarkers).ToArray() : markers,
                truncated,
                truncated ? $"Upstream window matched {markers.Count} applications; showing the first {MaxMarkers}." : null);
        }
#pragma warning disable CA1031 // The advanced path must degrade to snapshot-only, never throw.
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Upstream date-window search failed; falling back to snapshot activity");
            return new MarkersResult([], false, "Upstream date-window search is not available; showing cached snapshot activity only.");
        }
#pragma warning restore CA1031
    }

    private async Task<T> GetCachedAsync<T>(
        string key,
        TimeSpan ttl,
        Func<CancellationToken, Task<T>> load,
        Func<T, T> stale,
        T unavailable,
        CancellationToken cancellationToken)
        where T : class
    {
        // Manual expiry so an expired entry can still be served stale when refresh fails.
        if (_cache.TryGetValue(key, out CacheEntry<T>? entry) && entry is not null
            && DateTimeOffset.UtcNow - entry.FetchedUtc < ttl)
        {
            return entry.Value;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cache.TryGetValue(key, out entry) && entry is not null
                && DateTimeOffset.UtcNow - entry.FetchedUtc < ttl)
            {
                return entry.Value;
            }

            try
            {
                var fresh = await load(cancellationToken).ConfigureAwait(false);
                _cache.Set(key, new CacheEntry<T>(fresh, DateTimeOffset.UtcNow));
                return fresh;
            }
#pragma warning disable CA1031 // Cached content must degrade to stale/unavailable, never throw.
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Upstream fetch failed for {Key}", key);
                if (entry is not null)
                {
                    return stale(entry.Value);
                }

                return unavailable;
            }
#pragma warning restore CA1031
        }
        finally
        {
            _lock.Release();
        }
    }

    private sealed record CacheEntry<T>(T Value, DateTimeOffset FetchedUtc);

}
