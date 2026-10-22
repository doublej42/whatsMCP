using WhatsBuildingMcp.Services;

namespace WhatsBuildingMcp.Tests;

public sealed class CsvParsingTests
{
    private const string Header = "FileNumber,Url,DateSubmitted,CivicAddress,Neighbourhood,ApplicationType,StaffContact,Description,Status,PermitValue,BuildingArea,Applicants,IssuedDate,FinalDate,ExpiryDate,Properties,HasAttachments,HasNotices,Lat,Long";

    [Fact]
    public void Parses_Quoted_Commas_And_Newlines()
    {
        var csv = Header + "\n"
            + "\"DP001002\",\"https://example.test/folder/DP001002\",\"5/24/2016 12:00:00 AM\",\"1400 Wingrove Street\",\"Departure Bay\",\"Development Permit\",\"\",\"Two storey, mixed use building\nwith 4 units, and retail.\",\"ACTIVE\",\"\",\"\",\"\",\"\",\"\",\"\",\"1400 WINGROVE STREET\",\"True\",\"False\",\"49.2040495\",\"-123.9708757\"\n";

        var records = CsvSnapshotParser.Parse(new StringReader(csv));

        var record = Assert.Single(records);
        Assert.Equal("DP001002", record.FileNumber);
        Assert.Equal("Two storey, mixed use building\nwith 4 units, and retail.", record.Description);
        Assert.Equal(new DateTime(2016, 5, 24), record.DateSubmitted);
        Assert.True(record.HasAttachments);
        Assert.False(record.HasNotices);
        Assert.Equal(49.2040495, record.Latitude);
        Assert.Equal(-123.9708757, record.Longitude);
    }

    [Fact]
    public void Parses_Empty_Dates_And_Numbers_As_Null()
    {
        var csv = Header + "\n"
            + "\"BP1\",\"\",\"\",\"Addr\",\"Area\",\"Type\",\"\",\"Desc\",\"ACTIVE\",\"\",\"\",\"\",\"\",\"\",\"\",\"\",\"False\",\"False\",\"\",\"\"\n";

        var record = Assert.Single(CsvSnapshotParser.Parse(new StringReader(csv)));

        Assert.Null(record.DateSubmitted);
        Assert.Null(record.IssuedDate);
        Assert.Null(record.FinalDate);
        Assert.Null(record.ExpiryDate);
        Assert.Null(record.PermitValue);
        Assert.Null(record.BuildingArea);
        Assert.Null(record.Latitude);
        Assert.Null(record.Longitude);
        Assert.Empty(record.Applicants);
        Assert.Empty(record.Properties);
    }

    [Fact]
    public void Tolerates_Trailing_Comma_Header()
    {
        var csv = Header + ",\n"
            + "\"BP1\",\"\",\"\",\"Addr\",\"Area\",\"Type\",\"\",\"Desc\",\"ACTIVE\",\"\",\"\",\"\",\"\",\"\",\"\",\"\",\"False\",\"False\",\"49.1\",\"-123.9\",\n";

        var record = Assert.Single(CsvSnapshotParser.Parse(new StringReader(csv)));

        Assert.Equal("BP1", record.FileNumber);
        Assert.Equal(49.1, record.Latitude);
    }

    [Fact]
    public void Tolerates_Missing_And_Extra_Columns()
    {
        var csv = "FileNumber,CivicAddress,Status,ExtraColumn\n"
            + "\"BP1\",\"Addr\",\"ACTIVE\",\"whatever\"\n";

        var record = Assert.Single(CsvSnapshotParser.Parse(new StringReader(csv)));

        Assert.Equal("BP1", record.FileNumber);
        Assert.Equal("ACTIVE", record.Status);
        Assert.Equal(string.Empty, record.Description);
        Assert.Null(record.PermitValue);
    }

    [Fact]
    public void Skips_Rows_Without_File_Number()
    {
        var csv = Header + "\n"
            + "\"\",\"\",\"\",\"Addr\",\"Area\",\"Type\",\"\",\"Desc\",\"ACTIVE\",\"\",\"\",\"\",\"\",\"\",\"\",\"\",\"False\",\"False\",\"\",\"\"\n"
            + "\"BP2\",\"\",\"\",\"Addr\",\"Area\",\"Type\",\"\",\"Desc\",\"ACTIVE\",\"\",\"\",\"\",\"\",\"\",\"\",\"\",\"False\",\"False\",\"\",\"\"\n";

        var records = CsvSnapshotParser.Parse(new StringReader(csv));

        var record = Assert.Single(records);
        Assert.Equal("BP2", record.FileNumber);
    }

    [Fact]
    public void Parses_Applicants_And_Properties_Lists()
    {
        var csv = Header + "\n"
            + "\"BP1\",\"\",\"\",\"Addr\",\"Area\",\"Type\",\"\",\"Desc\",\"ACTIVE\",\"125000.50\",\"250.5\",\"Agent:Jane Doe , Owner:Bob Smith\",\"1/2/2020 12:00:00 AM\",\"\",\"\",\"155 FRY STREET , 157 FRY STREET\",\"False\",\"False\",\"\",\"\"\n";

        var record = Assert.Single(CsvSnapshotParser.Parse(new StringReader(csv)));

        Assert.Equal(125000.50m, record.PermitValue);
        Assert.Equal(250.5, record.BuildingArea);
        Assert.Equal(new DateTime(2020, 1, 2), record.IssuedDate);
        Assert.Collection(
            record.Applicants,
            a => Assert.Equal(("Agent", "Jane Doe"), (a.Role, a.Name)),
            a => Assert.Equal(("Owner", "Bob Smith"), (a.Role, a.Name)));
        Assert.Equal(["155 FRY STREET", "157 FRY STREET"], record.Properties);
    }

    [Fact]
    public void Empty_Input_Yields_No_Records()
    {
        Assert.Empty(CsvSnapshotParser.Parse(new StringReader(string.Empty)));
        Assert.Empty(CsvSnapshotParser.Parse(new StringReader(Header + "\n")));
    }
}
