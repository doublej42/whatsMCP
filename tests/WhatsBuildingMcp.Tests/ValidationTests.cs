using WhatsBuildingMcp.Models;
using WhatsBuildingMcp.Services;

namespace WhatsBuildingMcp.Tests;

public sealed class ValidationTests
{
    [Theory]
    [InlineData("DP001002", true)]
    [InlineData("RA000496", true)]
    [InlineData("SUB01118", true)]
    [InlineData("DVP00496", true)]
    [InlineData("A-1_2", true)]
    [InlineData("AB", false)]
    [InlineData("DP001002/../../Identity/Login", false)]
    [InlineData("DP 1002", false)]
    [InlineData("DP1002.KML", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("https://evil.example/x", false)]
    public void FileNumber_Validation(string? fileNumber, bool expected)
    {
        Assert.Equal(expected, InputValidation.IsValidFileNumber(fileNumber));
    }

    [Fact]
    public void CleanSearchText_Trims_Strips_Controls_And_Clamps()
    {
        Assert.Equal(string.Empty, InputValidation.CleanSearchText(null));
        Assert.Equal("abc", InputValidation.CleanSearchText("  a\tb\rc\n "));
        Assert.Equal(10, InputValidation.CleanSearchText(new string('x', 50), 10).Length);
    }

    [Theory]
    [InlineData("9/18/2012 12:00:00 AM", 2012, 9, 18)]
    [InlineData("May 24, 2016", 2016, 5, 24)]
    [InlineData("6/1/2016 11:02:31 AM", 2016, 6, 1)]
    [InlineData("2026-10-20", 2026, 10, 20)]
    [InlineData("Oct 20, 2026", 2026, 10, 20)]
    public void Parses_Upstream_Dates(string text, int year, int month, int day)
    {
        var parsed = DateUtil.ParseUpstreamDate(text);

        Assert.NotNull(parsed);
        Assert.Equal(new DateTime(year, month, day), parsed.Value.Date);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("not a date", false)]
    [InlineData(null, false)]
    public void Rejects_Bad_Dates(string? text, bool _)
    {
        Assert.Null(DateUtil.ParseUpstreamDate(text));
        Assert.Null(DateUtil.ParseDateParam(text));
    }

    [Theory]
    [InlineData("1", TypeCategory.Development)]
    [InlineData("development applications", TypeCategory.Development)]
    [InlineData("2", TypeCategory.BuildingPermits)]
    [InlineData("permit", TypeCategory.BuildingPermits)]
    [InlineData("3", TypeCategory.SubdivisionOther)]
    [InlineData("Subdivision", TypeCategory.SubdivisionOther)]
    [InlineData("bogus", null)]
    [InlineData("", null)]
    public void Parses_Type_Categories(string? text, int? expected)
    {
        Assert.Equal(expected, TypeCategory.Parse(text));
    }
}
