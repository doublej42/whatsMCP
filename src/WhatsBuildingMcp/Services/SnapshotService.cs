using WhatsBuildingMcp.Options;

namespace WhatsBuildingMcp.Services;

/// <summary>Provides the cached CSV snapshots, refreshing them in the background.</summary>
public interface ISnapshotService
{
    /// <summary>
    /// Returns the current snapshot. By default this is the all-folders snapshot (active
    /// plus inactive/completed, fetched with <c>SearchCriteria.ShowInactiveFolders=true</c>);
    /// pass <c>includeInactive: false</c> for the active-folders snapshot only. When the
    /// all-folders snapshot is unavailable, the active snapshot is returned with a warning.
    /// </summary>
    Task<Snapshot> GetSnapshotAsync(bool includeInactive = true, CancellationToken cancellationToken = default);
}

/// <summary>
/// Background service that downloads <c>whatsbuilding.csv</c> every <c>CacheMinutes</c>
/// twice — the default active set via GET, and the all-folders set via a search-form POST
/// with <c>SearchCriteria.ShowInactiveFolders=true</c> — and serves the immutable snapshots.
/// A failed refresh keeps serving the previous snapshots (marked stale); concurrent loads
/// are de-duplicated. When the all-folders snapshot is unavailable, callers asking for it
/// get the active snapshot with a warning instead of an error.
/// </summary>
public sealed class SnapshotService : BackgroundService, ISnapshotService
{
    private readonly WhatsBuildingClient _client;
    private readonly IReferenceDataService _referenceData;
    private readonly WhatsBuildingOptions _options;
    private readonly ILogger<SnapshotService> _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private Snapshot? _active;
    private Snapshot? _all;

    public SnapshotService(
        WhatsBuildingClient client,
        IReferenceDataService referenceData,
        IOptions<WhatsBuildingOptions> options,
        ILogger<SnapshotService> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(referenceData);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _client = client;
        _referenceData = referenceData;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Snapshot> GetSnapshotAsync(bool includeInactive = true, CancellationToken cancellationToken = default)
    {
        if (_active is { } snapshot)
        {
            return PickSnapshot(snapshot, includeInactive);
        }

        await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_active is null)
            {
                await LoadAsync(cancellationToken).ConfigureAwait(false);
            }

            return PickSnapshot(_active!, includeInactive);
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public override void Dispose()
    {
        _loadLock.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RefreshAsync(stoppingToken).ConfigureAwait(false);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_options.CacheMinutes));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await RefreshAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private Snapshot PickSnapshot(Snapshot active, bool includeInactive)
    {
        if (!includeInactive || _all is not null)
        {
            return includeInactive ? _all! : active;
        }

        return active with
        {
            Warning = JoinWarning(active.Warning, "Inactive folders are unavailable; showing active applications only."),
        };
    }

    private async Task RefreshAsync(CancellationToken stoppingToken)
    {
        await _loadLock.WaitAsync(stoppingToken).ConfigureAwait(false);
        try
        {
            await LoadAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // A failed refresh must never kill the background loop.
        catch (Exception ex)
        {
            MarkActiveStale($"Scheduled refresh failed ({ex.GetType().Name}); serving the previous snapshot.");
            _logger.LogWarning(ex, "Scheduled snapshot refresh failed; keeping the previous snapshot");
        }
#pragma warning restore CA1031
        finally
        {
            _loadLock.Release();
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        await LoadActiveAsync(cancellationToken).ConfigureAwait(false);
        await LoadAllAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task LoadActiveAsync(CancellationToken cancellationToken)
    {
        try
        {
            var csv = await _client.GetCsvAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StringReader(csv);
            var records = CsvSnapshotParser.Parse(reader, _logger);
            _active = new Snapshot(records, DateTimeOffset.UtcNow, false, null);
            _logger.LogInformation("Snapshot refreshed: {Count} active applications", records.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Snapshot loading must degrade to stale/empty, never throw.
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Snapshot load failed");
            if (_active is not null)
            {
                MarkActiveStale("Refresh failed; serving the previous snapshot.");
                return;
            }

            _active = new Snapshot([], DateTimeOffset.UtcNow, true, "Upstream data is unavailable and no snapshot has been loaded yet.");
        }
#pragma warning restore CA1031
    }

    private async Task LoadAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var reference = await _referenceData.GetReferenceDataAsync(cancellationToken).ConfigureAwait(false);
            if (!reference.IsLive || reference.FormDefaults.Count == 0)
            {
                _logger.LogWarning("All-folders snapshot skipped: the upstream search form is unavailable");
                MarkAllStale();
                return;
            }

            var csv = await _client.PostCsvAsync(reference.FormDefaults, cancellationToken).ConfigureAwait(false);
            if (!LooksLikeCsv(csv))
            {
                throw new UpstreamException("Upstream all-folders CSV did not contain the expected header row.");
            }

            using var reader = new StringReader(csv);
            var records = CsvSnapshotParser.Parse(reader, _logger);
            var activeCount = _active?.Records.Count ?? 0;
            if (records.Count < activeCount)
            {
                throw new UpstreamException(
                    $"Upstream all-folders CSV holds {records.Count} records, fewer than the {activeCount} active records; ignoring it.");
            }

            _all = new Snapshot(records, DateTimeOffset.UtcNow, false, null);
            _logger.LogInformation("All-folders snapshot refreshed: {Count} applications", records.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // All-folders loading must degrade to active-only, never throw.
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "All-folders snapshot load failed; falling back to the active snapshot");
            MarkAllStale();
        }
#pragma warning restore CA1031
    }

    private static bool LooksLikeCsv(string body)
    {
        using var reader = new StringReader(body);
        string? line;
        do
        {
            line = reader.ReadLine();
        }
        while (line is not null && line.Length == 0);

        return line is not null && line.Contains("FileNumber", StringComparison.Ordinal);
    }

    private void MarkActiveStale(string warning)
    {
        if (_active is { } active)
        {
            _active = active with { IsStale = true, Warning = warning };
        }
    }

    private void MarkAllStale()
    {
        if (_all is { } all)
        {
            _all = all with { IsStale = true, Warning = "Inactive folders refresh failed; serving the previous all-folders snapshot." };
        }
    }

    private static string JoinWarning(string? first, string second)
        => first is null ? second : first + " " + second;
}
