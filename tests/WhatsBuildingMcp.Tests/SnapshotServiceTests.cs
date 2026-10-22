using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
        using var service = new SnapshotService(client, options, NullLogger<SnapshotService>.Instance);

        await RunHostedOnceAsync(service);
        var fresh = await service.GetSnapshotAsync();
        Assert.False(fresh.IsStale);
        Assert.Single(fresh.Records);

        handler.Fail = true;
        await RunHostedOnceAsync(service);

        var stale = await service.GetSnapshotAsync();
        Assert.True(stale.IsStale);
        Assert.NotNull(stale.Warning);
        Assert.Single(stale.Records);
        Assert.Equal("DP1", stale.Records[0].FileNumber);
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
