using Microsoft.Extensions.Caching.Memory;
using WhatsBuildingMcp.Models;
using WhatsBuildingMcp.Options;

namespace WhatsBuildingMcp.Services;

/// <summary>Merges snapshot rows with parsed FolderView detail pages.</summary>
public interface IDetailService
{
    /// <summary>Returns null when the file number is unknown and not public.</summary>
    Task<ApplicationDetail?> GetApplicationAsync(string fileNumber, CancellationToken cancellationToken = default);
}

/// <summary>Cached per-application detail with request de-duplication.</summary>
public sealed class DetailService : IDetailService, IDisposable
{
    private const int Stripes = 16;

    private readonly IMemoryCache _cache;
    private readonly WhatsBuildingClient _client;
    private readonly ISnapshotService _snapshots;
    private readonly IReferenceDataService _referenceData;
    private readonly WhatsBuildingOptions _options;
    private readonly ILogger<DetailService> _logger;
    private readonly SemaphoreSlim[] _locks;
    private bool _disposed;

    public DetailService(
        IMemoryCache cache,
        WhatsBuildingClient client,
        ISnapshotService snapshots,
        IReferenceDataService referenceData,
        IOptions<WhatsBuildingOptions> options,
        ILogger<DetailService> logger)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(referenceData);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _cache = cache;
        _client = client;
        _snapshots = snapshots;
        _referenceData = referenceData;
        _options = options.Value;
        _logger = logger;
        _locks = Enumerable.Range(0, Stripes).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    }

    public async Task<ApplicationDetail?> GetApplicationAsync(string fileNumber, CancellationToken cancellationToken = default)
    {
        if (!InputValidation.IsValidFileNumber(fileNumber))
        {
            throw new ArgumentException($"Invalid file number '{fileNumber}'.", nameof(fileNumber));
        }

        var key = "whatsbuilding:detail:" + fileNumber.ToUpperInvariant();
        if (_cache.TryGetValue(key, out ApplicationDetail? cached))
        {
            return cached;
        }

        var stripe = _locks[(uint)key.GetHashCode(StringComparison.Ordinal) % Stripes];
        await stripe.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cache.TryGetValue(key, out cached))
            {
                return cached;
            }

            var detail = await LoadAsync(fileNumber, cancellationToken).ConfigureAwait(false);
            var ttl = detail is null
                ? TimeSpan.FromMinutes(5) // negative cache: unknown files stay cheap
                : TimeSpan.FromMinutes(_options.DetailCacheMinutes);
            _cache.Set(key, detail, ttl);
            return detail;
        }
        finally
        {
            stripe.Release();
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            foreach (var stripe in _locks)
            {
                stripe.Dispose();
            }

            _disposed = true;
        }
    }

    private async Task<ApplicationDetail?> LoadAsync(string fileNumber, CancellationToken cancellationToken)
    {
        var snapshot = await _snapshots.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var record = snapshot.Records.FirstOrDefault(r => r.FileNumber.Equals(fileNumber, StringComparison.OrdinalIgnoreCase));

        FolderViewDetail? page = null;
        string? warning = snapshot.Warning;
        try
        {
            var html = await _client.GetFolderViewAsync(fileNumber, cancellationToken).ConfigureAwait(false);
            page = html is null
                ? FolderViewDetail.NotFound(fileNumber)
                : FolderViewParser.Parse(html, fileNumber, _client.BaseUri);
        }
#pragma warning disable CA1031 // Detail enrichment must degrade to CSV-only, never throw.
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "FolderView fetch failed for {FileNumber}; falling back to snapshot data", fileNumber);
            warning = JoinWarning(warning, "The detail page could not be fetched; showing cached summary data only.");
        }
#pragma warning restore CA1031

        if (record is null && (page is null || !page.Found))
        {
            return null;
        }

        var reference = await _referenceData.GetReferenceDataAsync(cancellationToken).ConfigureAwait(false);
        var age = SnapshotAge(snapshot);
        var typeName = page?.ApplicationType is { Length: > 0 } t ? t : record?.ApplicationType ?? string.Empty;
        var category = reference.CategoryFor(typeName) ?? ReferenceDataFallback.CategoryForName(typeName);

        return new ApplicationDetail(
            FileNumber: record?.FileNumber ?? page?.FileNumber ?? fileNumber,
            Found: true,
            Url: record?.Url ?? _client.BaseUri.AbsoluteUri.TrimEnd('/') + "/Folder/" + Uri.EscapeDataString(fileNumber),
            CivicAddress: page?.CivicAddress is { Length: > 0 } a ? a : record?.CivicAddress ?? string.Empty,
            Neighbourhood: page?.Neighbourhood is { Length: > 0 } n ? n : record?.Neighbourhood ?? string.Empty,
            ApplicationType: typeName,
            TypeCategory: category.HasValue ? Models.TypeCategory.Name(category.Value) : null,
            Status: page?.Status is { Length: > 0 } s ? s : record?.Status ?? string.Empty,
            Description: page?.Description is { Length: > 0 } d ? d : TextUtil.NullIfEmpty(record?.Description),
            DateSubmitted: page?.DateSubmitted ?? record?.DateSubmitted,
            IssuedDate: page?.IssuedDate ?? record?.IssuedDate,
            FinalDate: page?.CloseDate ?? record?.FinalDate,
            ExpiryDate: page?.ExpiryDate ?? record?.ExpiryDate,
            PermitValue: record?.PermitValue,
            BuildingArea: record?.BuildingArea,
            StaffContact: page?.StaffContact is { Length: > 0 } c ? c : TextUtil.NullIfEmpty(record?.StaffContact),
            Applicants: record?.Applicants ?? [],
            Properties: record?.Properties ?? [],
            PropertyDetails: page?.Properties ?? [],
            Attachments: page?.Attachments ?? [],
            Notices: page?.Notices ?? [],
            EducationalNote: page?.EducationalNote,
            ProcessSteps: page?.ProcessSteps ?? [],
            Policies: page?.Policies ?? [],
            Latitude: page?.Latitude ?? record?.Latitude,
            Longitude: page?.Longitude ?? record?.Longitude,
            SnapshotAgeSeconds: age,
            Warning: warning);
    }

    private static long SnapshotAge(Snapshot snapshot)
        => (long)Math.Max(0, (DateTimeOffset.UtcNow - snapshot.FetchedUtc).TotalSeconds);

    private static string? JoinWarning(string? first, string second)
        => first is null ? second : first + " " + second;
}
