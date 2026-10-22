using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WhatsBuildingMcp.Options;
using MOptions = Microsoft.Extensions.Options.Options;
using WhatsBuildingMcp.Services;
using WhatsBuildingMcp.Tools;

namespace WhatsBuildingMcp.Tests;

/// <summary>Tool-level tests over a fake upstream (no network).</summary>
public sealed class ToolsTests : IDisposable
{
    private const string Csv = """
        "FileNumber","Url","DateSubmitted","CivicAddress","Neighbourhood","ApplicationType","StaffContact","Description","Status","PermitValue","BuildingArea","Applicants","IssuedDate","FinalDate","ExpiryDate","Properties","HasAttachments","HasNotices","Lat","Long"
        "DP001002","https://www.nanaimo.ca/whatsbuilding/folder/DP001002","5/24/2016 12:00:00 AM","1400 Wingrove Street","Departure Bay","Development Permit","","Two storey mixed use building.","ACTIVE","","","","","","","1400 WINGROVE STREET","True","False","49.2040495","-123.9708757"
        "RA000496","https://www.nanaimo.ca/whatsbuilding/folder/RA000496","6/15/2023 12:00:00 AM","3469 Uplands Drive","Rutherford","Rezoning Application","Caleb Horn","Rezone to R8.","ACTIVE","250000","","Agent:Jane Doe","","","","3469 UPLANDS DRIVE","True","False","49.2149025","-124.0015826"
        """;

    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly FakeHandler _handler = new();
    private readonly WhatsBuildingTools _tools;

    public ToolsTests()
    {
        var options = MOptions.Create(new WhatsBuildingOptions
        {
            BaseUrl = "https://upstream.test/whatsbuilding/",
            CacheMinutes = 30,
        });
        var guard = new OutboundHostGuardHandler(options) { InnerHandler = _handler };
        var http = new HttpClient(guard) { BaseAddress = new Uri("https://upstream.test/whatsbuilding/") };
        var client = new WhatsBuildingClient(http, options, NullLogger<WhatsBuildingClient>.Instance);
        var snapshots = new SnapshotService(client, options, NullLogger<SnapshotService>.Instance);
        var reference = new ReferenceDataService(_cache, client, snapshots, options, NullLogger<ReferenceDataService>.Instance);
        var details = new DetailService(_cache, client, snapshots, reference, options, NullLogger<DetailService>.Instance);
        var content = new UpstreamContentService(_cache, client, reference, options, NullLogger<UpstreamContentService>.Instance);
        _tools = new WhatsBuildingTools(snapshots, reference, details, content);

        _handler.Responses["/whatsbuilding/whatsbuilding.csv"] = Csv;
        _handler.Responses["/whatsbuilding/FolderView/DP001002"] = Fixture("folderview_dp001002.html");
        _handler.Responses["/whatsbuilding/"] = Fixture("searchform_fragment.html");
    }

    public void Dispose()
    {
        _cache.Dispose();
        _handler.Dispose();
    }

    [Fact]
    public async Task Search_Returns_Paged_Envelope()
    {
        var json = await _tools.SearchApplicationsAsync(neighbourhood: "Rutherford");

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("total").GetInt64());
        Assert.Equal(1, root.GetProperty("returned").GetInt32());
        Assert.Equal("RA000496", root.GetProperty("items")[0].GetProperty("fileNumber").GetString());
        Assert.Equal("Development Applications", root.GetProperty("items")[0].GetProperty("typeCategory").GetString());
        Assert.True(root.GetProperty("snapshotAgeSeconds").GetInt64() >= 0);

        // Every tool call reuses the snapshot: the CSV was fetched exactly once.
        Assert.Equal(2, _handler.Requests.Count); // CSV + search form
        await _tools.SearchApplicationsAsync(keyword: "mixed use");
        Assert.Equal(2, _handler.Requests.Count);
    }

    [Fact]
    public async Task GetApplication_Merges_Snapshot_And_Detail()
    {
        var json = await _tools.GetApplicationAsync("DP001002");

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("DP001002", root.GetProperty("fileNumber").GetString());
        Assert.True(root.GetProperty("found").GetBoolean());
        Assert.Equal("Departure Bay", root.GetProperty("neighbourhood").GetString());
        Assert.Contains("two storey", root.GetProperty("description").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(root.GetProperty("attachments").EnumerateArray());
        Assert.NotEmpty(root.GetProperty("properties").EnumerateArray());
    }

    [Fact]
    public async Task GetApplication_Unknown_File_Returns_NotFound()
    {
        var json = await _tools.GetApplicationAsync("ZZ999999");

        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.GetProperty("found").GetBoolean());
    }

    [Fact]
    public async Task GetApplication_Rejects_Invalid_FileNumber()
    {
        var json = await _tools.GetApplicationAsync("../Identity/Login");

        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.GetProperty("found").GetBoolean());
        Assert.DoesNotContain(_handler.Requests, r => r.RequestUri!.AbsolutePath.Contains("Identity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Find_Near_Orders_By_Distance()
    {
        var json = await _tools.FindApplicationsNearAsync(49.2149, -124.0016, 50000);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(2, root.GetProperty("total").GetInt64());
        Assert.Equal("RA000496", root.GetProperty("items")[0].GetProperty("application").GetProperty("fileNumber").GetString());
    }

    [Fact]
    public async Task No_Request_Ever_Leaves_The_Configured_Host()
    {
        await _tools.SearchApplicationsAsync();
        await _tools.GetApplicationAsync("DP001002");
        await _tools.ListNeighbourhoodsAsync();
        await _tools.ListApplicationTypesAsync();

        Assert.NotEmpty(_handler.Requests);
        Assert.All(_handler.Requests, r => Assert.Equal("upstream.test", r.RequestUri!.Host));
        Assert.DoesNotContain(_handler.Requests, r => r.RequestUri!.AbsolutePath.Contains("Identity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Failed_First_Load_Yields_Warning_Envelope()
    {
        _handler.Responses.Clear(); // every upstream call 404s
        var options = MOptions.Create(new WhatsBuildingOptions { BaseUrl = "https://upstream.test/whatsbuilding/" });
        var guard = new OutboundHostGuardHandler(options) { InnerHandler = _handler };
        var http = new HttpClient(guard) { BaseAddress = new Uri("https://upstream.test/whatsbuilding/") };
        var client = new WhatsBuildingClient(http, options, NullLogger<WhatsBuildingClient>.Instance);
        var snapshots = new SnapshotService(client, options, NullLogger<SnapshotService>.Instance);
        var reference = new ReferenceDataService(new MemoryCache(new MemoryCacheOptions()), client, snapshots, options, NullLogger<ReferenceDataService>.Instance);
        var details = new DetailService(new MemoryCache(new MemoryCacheOptions()), client, snapshots, reference, options, NullLogger<DetailService>.Instance);
        var content = new UpstreamContentService(new MemoryCache(new MemoryCacheOptions()), client, reference, options, NullLogger<UpstreamContentService>.Instance);
        var tools = new WhatsBuildingTools(snapshots, reference, details, content);

        var json = await tools.SearchApplicationsAsync();

        using var document = JsonDocument.Parse(json);
        Assert.Equal(0, document.RootElement.GetProperty("total").GetInt64());
        Assert.NotNull(document.RootElement.GetProperty("warning").GetString());
    }

    private static string Fixture(string name)
        => File.ReadAllText(Path.Combine("Fixtures", name));

    private sealed class FakeHandler : HttpMessageHandler
    {
        public Dictionary<string, string> Responses { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.RequestUri is not null && Responses.TryGetValue(request.RequestUri.AbsolutePath, out var body))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
