using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WhatsBuildingMcp.Models;
using WhatsBuildingMcp.Options;
using MOptions = Microsoft.Extensions.Options.Options;
using WhatsBuildingMcp.Services;

namespace WhatsBuildingMcp.Tests;

public sealed class SnapshotServiceTests
{
    private const string Csv = """
        "FileNumber","Url","DateSubmitted","CivicAddress","Neighbourhood","ApplicationType","StaffContact","Description","Status","PermitValue","BuildingArea","Applicants","IssuedDate","FinalDate","ExpiryDate","Properties","HasAttachments","HasNotices","Lat","Long"
        "DP1","","","","Downtown","","","Desc","ACTIVE","","","","","","","","False","False","",""
        """;

    [Fact]
    public async Task Failed_Refresh_Keeps_Serving_Stale_Snapshot()
    {
        var handler = new ToggleHandler { Body = Csv };
        var options = MOptions.Create(new WhatsBuildingOptions
        {
            BaseUrl = "https://upstream.test/whatsbuilding/",
            CacheMinutes = 60,
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://upstream.test/whatsbuilding/") };
        var client = new WhatsBuildingClient(http, options, NullLogger<WhatsBuildingClient>.Instance);
        var reference = new StubReferenceDataService(new ReferenceData([], [], [], false, DateTimeOffset.UtcNow));
        using var service = new SnapshotService(client, reference, options, NullLogger<SnapshotService>.Instance);

        await RunHostedOnceAsync(service);
        var fresh = await service.GetSnapshotAsync();
        Assert.False(fresh.IsStale);
        Assert.Single(fresh.Records);

        // No live search form, so the default all-folders request falls back to active.
        Assert.Contains("active", fresh.Warning, StringComparison.OrdinalIgnoreCase);

        handler.Fail = true;
        await RunHostedOnceAsync(service);

        var stale = await service.GetSnapshotAsync();
        Assert.True(stale.IsStale);
        Assert.NotNull(stale.Warning);
        Assert.Single(stale.Records);
        Assert.Equal("DP1", stale.Records[0].FileNumber);
    }

    [Fact]
    public async Task AllFolders_Snapshot_Served_When_Form_Post_Succeeds()
    {
        var handler = new ToggleHandler { Body = Csv };
        var options = MOptions.Create(new WhatsBuildingOptions
        {
            BaseUrl = "https://upstream.test/whatsbuilding/",
            CacheMinutes = 60,
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://upstream.test/whatsbuilding/") };
        var client = new WhatsBuildingClient(http, options, NullLogger<WhatsBuildingClient>.Instance);
        var reference = new StubReferenceDataService(new ReferenceData(
            [],
            [],
            [new FormField("SearchCriteria.ShowInactiveFolders", "false")],
            true,
            DateTimeOffset.UtcNow));
        using var service = new SnapshotService(client, reference, options, NullLogger<SnapshotService>.Instance);

        var all = await service.GetSnapshotAsync();
        Assert.Single(all.Records);
        Assert.False(all.IsStale);
        Assert.Null(all.Warning);

        var active = await service.GetSnapshotAsync(includeInactive: false);
        Assert.Single(active.Records);
        Assert.False(active.IsStale);
        Assert.Null(active.Warning);
    }

    private static async Task RunHostedOnceAsync(SnapshotService service)
    {
        // Runs the hosted loop long enough for one refresh, then stops it.
        using var lifetime = new CancellationTokenSource();
        await service.StartAsync(lifetime.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken.None);
        await lifetime.CancelAsync();
        try
        {
            await service.StopAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed class StubReferenceDataService(ReferenceData data) : IReferenceDataService
    {
        public Task<ReferenceData> GetReferenceDataAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(data);
    }

    private sealed class ToggleHandler : HttpMessageHandler
    {
        public string? Body { get; set; }

        public bool Fail { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Fail || Body is null)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Body) });
        }
    }
}
