using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WhatsBuildingMcp.Models;
using WhatsBuildingMcp.Options;
using MOptions = Microsoft.Extensions.Options.Options;
using WhatsBuildingMcp.Services;
using Xunit.Abstractions;

namespace WhatsBuildingMcp.Tests;

/// <summary>
/// Live upstream parity test: verifies the all-folders CSV and the all-folders marker
/// set describe the same population. Opt-in only — runs only when
/// <c>WHATSBUILDING_LIVE_TEST=1</c>; otherwise passes without touching the network.
/// </summary>
public sealed class LiveParityTests
{
    // Observed exactly 65,733 results on both endpoints on 2026-10-23; the ±10% band
    // below allows for upstream drift. Recalibrate if the live population moves for good.
    private const int ExpectedAllFolders = 65733;

    private static readonly JsonSerializerOptions MarkerJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ITestOutputHelper _output;

    public LiveParityTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Live_AllFolders_Markers_And_Csv_Return_Similar_Results()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("WHATSBUILDING_LIVE_TEST"), "1", StringComparison.Ordinal))
        {
            return; // Hermetic run: skip the live upstream check.
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("WhatsBuildingMcp/1.0 (+https://www.nanaimo.ca/whatsbuilding/)");
        var options = MOptions.Create(new WhatsBuildingOptions
        {
            // Test-only caps: the all-folders payloads dwarf production windows.
            MaxCsvBytes = 256L * 1024 * 1024,
            MaxResponseBytes = 256L * 1024 * 1024,
        });
        var client = new WhatsBuildingClient(http, options, NullLogger<WhatsBuildingClient>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var home = await client.GetHomeHtmlAsync(cts.Token);
        var reference = ReferenceDataParser.Parse(home);
        Assert.True(reference.IsLive, "Upstream search form scrape failed; cannot build the all-folders POSTs.");
        Assert.NotEmpty(reference.FormDefaults);

        var csv = await client.PostCsvAsync(reference.FormDefaults, cts.Token);
        using var reader = new StringReader(csv);
        var records = CsvSnapshotParser.Parse(reader);
        var csvFiles = new HashSet<string>(
            records.Select(r => r.FileNumber).Where(f => f.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        var markersJson = await client.PostMarkersAsync(reference.FormDefaults, cancellationToken: cts.Token);
        var markers = JsonSerializer.Deserialize<List<Marker>>(markersJson, MarkerJson) ?? [];
        var markerFiles = new HashSet<string>(
            markers.Select(m => m.FileNumber).Where(f => f.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        var overlap = csvFiles.Intersect(markerFiles, StringComparer.OrdinalIgnoreCase).Count();
        _output.WriteLine($"csv: {records.Count} records ({csv.Length} chars, {csvFiles.Count} file numbers)");
        _output.WriteLine($"markers: {markers.Count} entries ({markersJson.Length} chars, {markerFiles.Count} file numbers)");
        _output.WriteLine($"file-number overlap: {overlap}");

        var low = (int)(ExpectedAllFolders * 0.9);
        var high = (int)(ExpectedAllFolders * 1.1);
        Assert.InRange(records.Count, low, high);
        Assert.InRange(markers.Count, low, high);

        var ratio = Math.Abs(records.Count - markers.Count) / (double)Math.Max(records.Count, markers.Count);
        Assert.True(ratio < 0.05, $"Endpoint counts diverged: csv={records.Count}, markers={markers.Count}.");

        Assert.True(
            overlap / (double)csvFiles.Count >= 0.90,
            $"Only {overlap} of {csvFiles.Count} CSV file numbers appear in the markers.");
        Assert.True(
            overlap / (double)markerFiles.Count >= 0.90,
            $"Only {overlap} of {markerFiles.Count} marker file numbers appear in the CSV.");
    }
}
