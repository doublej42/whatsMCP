using WhatsBuildingMcp.Options;

namespace WhatsBuildingMcp.Services;

/// <summary>Provides the cached CSV snapshot, refreshing it in the background.</summary>
public interface ISnapshotService
{
    /// <summary>Returns the current snapshot, loading it lazily on first use.</summary>
    Task<Snapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Background service that downloads <c>whatsbuilding.csv</c> every
/// <c>CacheMinutes</c> and serves the immutable snapshot. A failed refresh keeps
/// serving the previous snapshot (marked stale); concurrent loads are de-duplicated.
/// </summary>
public sealed class SnapshotService : BackgroundService, ISnapshotService
{
    private readonly WhatsBuildingClient _client;
    private readonly WhatsBuildingOptions _options;
    private readonly ILogger<SnapshotService> _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private Snapshot? _current;

    public SnapshotService(WhatsBuildingClient client, IOptions<WhatsBuildingOptions> options, ILogger<SnapshotService> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Snapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        if (_current is { } snapshot)
        {
            return snapshot;
        }

        await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _current ?? await LoadAsync(cancellationToken).ConfigureAwait(false);
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
            MarkStale($"Scheduled refresh failed ({ex.GetType().Name}); serving the previous snapshot.");
            _logger.LogWarning(ex, "Scheduled snapshot refresh failed; keeping the previous snapshot");
        }
#pragma warning restore CA1031
        finally
        {
            _loadLock.Release();
        }
    }

    private async Task<Snapshot> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var csv = await _client.GetCsvAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StringReader(csv);
            var records = CsvSnapshotParser.Parse(reader, _logger);
            _current = new Snapshot(records, DateTimeOffset.UtcNow, false, null);
            _logger.LogInformation("Snapshot refreshed: {Count} active applications", records.Count);
            return _current;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Snapshot loading must degrade to stale/empty, never throw.
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Snapshot load failed");
            if (_current is not null)
            {
                MarkStale("Refresh failed; serving the previous snapshot.");
                return _current;
            }

            _current = new Snapshot([], DateTimeOffset.UtcNow, true, "Upstream data is unavailable and no snapshot has been loaded yet.");
            return _current;
        }
#pragma warning restore CA1031
    }

    private void MarkStale(string warning)
    {
        if (_current is { } current)
        {
            _current = current with { IsStale = true, Warning = warning };
        }
    }
}
