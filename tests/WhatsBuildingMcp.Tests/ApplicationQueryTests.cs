using WhatsBuildingMcp.Models;
using WhatsBuildingMcp.Services;

namespace WhatsBuildingMcp.Tests;

public sealed class ApplicationQueryTests
{
    private static int? CategoryOf(string typeName) => typeName switch
    {
        "Development Permit" => TypeCategory.Development,
        "Single / Two Family Dwelling" => TypeCategory.BuildingPermits,
        "Subdivision" => TypeCategory.SubdivisionOther,
        _ => null,
    };

    private static ApplicationRecord Record(
        string fileNumber,
        string neighbourhood = "Downtown/Old City",
        string type = "Development Permit",
        string status = "ACTIVE",
        string address = "155 Fry Street",
        string description = "A building",
        DateTime? submitted = null,
        bool nullSubmitted = false,
        DateTime? issued = null,
        DateTime? final = null,
        decimal? value = null,
        string[]? properties = null,
        Applicant[]? applicants = null,
        double? lat = 49.1659,
        double? lon = -123.9401)
        => new(
            fileNumber,
            "https://example.test/" + fileNumber,
            nullSubmitted ? null : submitted ?? new DateTime(2024, 1, 15),
            address,
            neighbourhood,
            type,
            string.Empty,
            description,
            status,
            value,
            null,
            applicants ?? [],
            issued,
            final,
            null,
            properties ?? [],
            false,
            false,
            lat?.ToString(DateUtil.EnUs) ?? string.Empty,
            lon?.ToString(DateUtil.EnUs) ?? string.Empty,
            lat,
            lon);

    [Fact]
    public void Keyword_Matches_Address_Description_Applicants_And_Properties()
    {
        var records = new[]
        {
            Record("DP1", address: "155 Fry Street"),
            Record("DP2", address: "1 Main Street", description: "fry cook restaurant"),
            Record("DP3", address: "2 Main Street", applicants: [new Applicant("Agent", "Fry Guy")]),
            Record("DP4", address: "3 Main Street", properties: ["99 FRY LANE"]),
            Record("DP5", address: "4 Main Street", description: "unrelated"),
        };

        var (total, items, _) = ApplicationQuery.Apply(records, new ApplicationQueryParams(Keyword: "fRy"), CategoryOf);

        Assert.Equal(4, total);
        Assert.DoesNotContain(items, r => r.FileNumber == "DP5");
    }

    [Fact]
    public void FileNumber_Uses_Contains_Search()
    {
        var records = new[] { Record("DP001002"), Record("BP118537"), Record("RA000496") };

        var (total, items, _) = ApplicationQuery.Apply(records, new ApplicationQueryParams(FileNumber: "dp001"), CategoryOf);

        Assert.Equal(1, total);
        Assert.Equal("DP001002", items[0].FileNumber);
    }

    [Fact]
    public void Combines_Neighbourhood_Type_Category_And_Status()
    {
        var records = new[]
        {
            Record("DP1", neighbourhood: "South End", type: "Development Permit"),
            Record("DP2", neighbourhood: "South End", type: "Single / Two Family Dwelling", status: "COMPLETED"),
            Record("DP3", neighbourhood: "Harewood", type: "Development Permit"),
        };

        var southEnd = ApplicationQuery.Apply(records, new ApplicationQueryParams(Neighbourhood: "south end"), CategoryOf);
        Assert.Equal(2, southEnd.Total);

        var permits = ApplicationQuery.Apply(records, new ApplicationQueryParams(TypeCategory: TypeCategory.BuildingPermits), CategoryOf);
        Assert.Equal("DP2", Assert.Single(permits.Items).FileNumber);

        var completed = ApplicationQuery.Apply(records, new ApplicationQueryParams(Status: "completed"), CategoryOf);
        Assert.Equal("DP2", Assert.Single(completed.Items).FileNumber);
    }

    [Fact]
    public void Street_And_HouseNumber_Match_Address_And_Properties()
    {
        var records = new[]
        {
            Record("DP1", address: "155 Fry Street"),
            Record("DP2", address: "Lot 1", properties: ["155 FRY STREET"]),
            Record("DP3", address: "200 Fry Street"),
        };

        var byStreet = ApplicationQuery.Apply(records, new ApplicationQueryParams(Street: "fry street"), CategoryOf);
        Assert.Equal(3, byStreet.Total);

        var byHouse = ApplicationQuery.Apply(records, new ApplicationQueryParams(Street: "fry street", HouseNumber: "155"), CategoryOf);
        Assert.Equal(2, byHouse.Total);
    }

    [Fact]
    public void Date_Ranges_Are_Inclusive()
    {
        var records = new[]
        {
            Record("DP1", submitted: new DateTime(2024, 1, 10), issued: new DateTime(2024, 6, 1)),
            Record("DP2", submitted: new DateTime(2024, 2, 10), issued: new DateTime(2024, 7, 1)),
            Record("DP3", nullSubmitted: true),
        };

        var submitted = ApplicationQuery.Apply(
            records,
            new ApplicationQueryParams(SubmittedFrom: new DateTime(2024, 1, 10), SubmittedTo: new DateTime(2024, 2, 10)),
            CategoryOf);
        Assert.Equal(2, submitted.Total);

        var issued = ApplicationQuery.Apply(
            records,
            new ApplicationQueryParams(IssuedFrom: new DateTime(2024, 6, 1), IssuedTo: new DateTime(2024, 6, 30)),
            CategoryOf);
        Assert.Equal("DP1", Assert.Single(issued.Items).FileNumber);
    }

    [Fact]
    public void MinPermitValue_Filters_Nulls_Out()
    {
        var records = new[] { Record("DP1", value: 100m), Record("DP2", value: null), Record("DP3", value: 200m) };

        var (total, _, _) = ApplicationQuery.Apply(records, new ApplicationQueryParams(MinPermitValue: 150m), CategoryOf);

        Assert.Equal(1, total);
    }

    [Fact]
    public void Bylaw_Filter_Is_Ignored_With_Warning()
    {
        var records = new[] { Record("DP1") };

        var (total, _, warning) = ApplicationQuery.Apply(records, new ApplicationQueryParams(BylawRequested: true), CategoryOf);

        Assert.Equal(1, total);
        Assert.Contains("Bylaw", warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Default_Sort_Is_Neighbourhood_Then_FileNumber()
    {
        var records = new[]
        {
            Record("DP2", neighbourhood: "Harewood"),
            Record("DP1", neighbourhood: "Downtown/Old City"),
            Record("DP3", neighbourhood: "Downtown/Old City"),
        };

        var (_, items, _) = ApplicationQuery.Apply(records, new ApplicationQueryParams(), CategoryOf);

        Assert.Equal(["DP1", "DP3", "DP2"], items.Select(r => r.FileNumber));
    }

    [Fact]
    public void SortBy_Type_Folder_Date_And_Descending()
    {
        var records = new[]
        {
            Record("DP1", type: "Subdivision", submitted: new DateTime(2024, 1, 1)),
            Record("DP2", type: "Development Permit", submitted: new DateTime(2024, 3, 1)),
        };

        var byType = ApplicationQuery.Apply(records, new ApplicationQueryParams(SortBy: "type"), CategoryOf);
        Assert.Equal(["DP2", "DP1"], byType.Items.Select(r => r.FileNumber));

        var byDateDesc = ApplicationQuery.Apply(records, new ApplicationQueryParams(SortBy: "date", Descending: true), CategoryOf);
        Assert.Equal(["DP2", "DP1"], byDateDesc.Items.Select(r => r.FileNumber));

        var byFolderDesc = ApplicationQuery.Apply(records, new ApplicationQueryParams(SortBy: "folder", Descending: true), CategoryOf);
        Assert.Equal(["DP2", "DP1"], byFolderDesc.Items.Select(r => r.FileNumber));
    }

    [Fact]
    public void Paging_Clamps_Limit_And_Offset()
    {
        var records = Enumerable.Range(1, 10).Select(i => Record($"DP{i:000}")).ToArray();

        var page = ApplicationQuery.Apply(records, new ApplicationQueryParams(Offset: 8, Limit: 500), CategoryOf);
        Assert.Equal(10, page.Total);
        Assert.Equal(2, page.Items.Count);

        var empty = ApplicationQuery.Apply(records, new ApplicationQueryParams(Offset: 50), CategoryOf);
        Assert.Equal(10, empty.Total);
        Assert.Empty(empty.Items);
    }

    [Fact]
    public void Statistics_Group_Counts_And_Totals()
    {
        var records = new[]
        {
            Record("DP1", neighbourhood: "South End", value: 100m),
            Record("DP2", neighbourhood: "South End", value: 50m),
            Record("DP3", neighbourhood: "Harewood", value: null),
        };

        var groups = ApplicationQuery.GroupBy(records, "neighbourhood");

        Assert.Equal(2, groups.Count);
        Assert.Equal("South End", groups[0].Key);
        Assert.Equal(2, groups[0].Count);
        Assert.Equal(150m, groups[0].TotalPermitValue);
        Assert.Throws<ArgumentException>(() => ApplicationQuery.GroupBy(records, "bogus"));
    }

    [Fact]
    public void Recent_From_Snapshot_Uses_Window_And_Kind()
    {
        var today = new DateOnly(2026, 10, 20);
        var records = new[]
        {
            Record("DP1", issued: new DateTime(2026, 10, 18)),
            Record("DP2", issued: new DateTime(2026, 9, 1)),
            Record("DP3", final: new DateTime(2026, 10, 19)),
        };

        var issued = ApplicationQuery.RecentFromSnapshot(records, "issued", today, 7);
        Assert.Equal("DP1", Assert.Single(issued).FileNumber);
        Assert.Equal("snapshot", issued[0].Source);

        var completed = ApplicationQuery.RecentFromSnapshot(records, "completed", today, 7);
        Assert.Equal("DP3", Assert.Single(completed).FileNumber);

        Assert.Throws<ArgumentException>(() => ApplicationQuery.RecentFromSnapshot(records, "bogus", today, 7));
    }

    [Fact]
    public void Merge_Recent_Dedupes_By_FileNumber()
    {
        var today = new DateOnly(2026, 10, 20);
        var records = new[] { Record("DP1", issued: new DateTime(2026, 10, 18)) };
        var snapshotItems = ApplicationQuery.RecentFromSnapshot(records, "issued", today, 7);
        var markers = new[]
        {
            new Marker(FileNumber: "DP1", CivicAddress: "dup", Type1: "T", Status1: "ACTIVE"),
            new Marker(FileNumber: "RA9", CivicAddress: "new", Type1: "T", Status1: "COMPLETED"),
        };

        var merged = ApplicationQuery.MergeRecent(snapshotItems, markers, "issued", 25);

        Assert.Equal(2, merged.Count);
        Assert.Equal("snapshot", merged[0].Source);
        Assert.Equal("RA9", merged[1].FileNumber);
        Assert.Equal("upstream-window", merged[1].Source);
    }
}
