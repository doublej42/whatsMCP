using WhatsBuildingMcp.Services;

namespace WhatsBuildingMcp.Tests;

public sealed class ContentParserTests
{
    [Fact]
    public void Parses_Pending_Permits_Table()
    {
        var html = File.ReadAllText(Path.Combine("Fixtures", "pendingpermits_table.html"));

        var permits = PendingPermitsParser.Parse(html);

        Assert.Equal(6, permits.Count);
        var single = Assert.Single(permits, p => p.ApplicationType == "Single Family Residential");
        Assert.Equal(4, single.WaitingCount);
        Assert.Equal(new DateTime(2026, 9, 24), single.NextUpForReview);
        Assert.All(permits.Where(p => p.ApplicationType != "Single Family Residential"), p => Assert.Equal(0, p.WaitingCount));
    }

    [Fact]
    public void Extracts_About_Text()
    {
        var html = File.ReadAllText(Path.Combine("Fixtures", "about_fragment.html"));

        var text = AboutParser.ExtractText(html);

        Assert.Contains("provides up-to-date information about applications and permits", text, StringComparison.Ordinal);
        Assert.Contains("How to search and filter results", text, StringComparison.Ordinal);
        Assert.DoesNotContain('<', text);
    }
}
