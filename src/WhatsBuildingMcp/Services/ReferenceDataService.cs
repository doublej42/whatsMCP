using Microsoft.Extensions.Caching.Memory;
using WhatsBuildingMcp.Models;
using WhatsBuildingMcp.Options;

namespace WhatsBuildingMcp.Services;

/// <summary>Provides cached search-form reference data (types, areas, replayable form fields).</summary>
public interface IReferenceDataService
{
    Task<ReferenceData> GetReferenceDataAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Scrapes the home-page search form once and caches it. Falls back to
/// snapshot-derived names plus the built-in category map when scraping fails.
/// </summary>
public sealed class ReferenceDataService : IReferenceDataService, IDisposable
{
    private const string CacheKey = "whatsbuilding:referencedata";

    private readonly IMemoryCache _cache;
    private readonly WhatsBuildingClient _client;
    private readonly ISnapshotService _snapshots;
    private readonly WhatsBuildingOptions _options;
    private readonly ILogger<ReferenceDataService> _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private bool _disposed;

    public ReferenceDataService(
        IMemoryCache cache,
        WhatsBuildingClient client,
        ISnapshotService snapshots,
        IOptions<WhatsBuildingOptions> options,
        ILogger<ReferenceDataService> logger)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _cache = cache;
        _client = client;
        _snapshots = snapshots;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ReferenceData> GetReferenceDataAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(CacheKey, out ReferenceData? cached) && cached is not null)
        {
            return cached;
        }

        await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cache.TryGetValue(CacheKey, out cached) && cached is not null)
            {
                return cached;
            }

            var data = await LoadAsync(cancellationToken).ConfigureAwait(false);
            _cache.Set(CacheKey, data, TimeSpan.FromHours(_options.ReferenceDataCacheHours));
            return data;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _loadLock.Dispose();
            _disposed = true;
        }
    }

    private async Task<ReferenceData> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var html = await _client.GetHomeHtmlAsync(cancellationToken).ConfigureAwait(false);
            var parsed = ReferenceDataParser.Parse(html, _logger);
            if (parsed.IsLive)
            {
                _logger.LogInformation(
                    "Reference data scraped: {Types} types, {Areas} areas",
                    parsed.Types.Count,
                    parsed.Areas.Count);
                return parsed;
            }

            _logger.LogWarning("Scraped reference data was empty; using the snapshot-derived fallback");
        }
#pragma warning disable CA1031 // Reference-data load must degrade to the fallback, never throw.
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Reference-data scrape failed; using the snapshot-derived fallback");
        }
#pragma warning restore CA1031

        var snapshot = await _snapshots.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var types = snapshot.Records
            .Select(r => r.ApplicationType)
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Select(t => new ReferenceType(0, t, ReferenceDataFallback.CategoryForName(t) ?? 0))
            .ToArray();
        var areas = snapshot.Records
            .Select(r => r.Neighbourhood)
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(n => new ReferenceArea(0, n))
            .ToArray();
        return new ReferenceData(types, areas, [], false, DateTimeOffset.UtcNow);
    }
}
