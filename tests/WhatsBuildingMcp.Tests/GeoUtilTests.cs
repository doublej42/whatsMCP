using WhatsBuildingMcp.Services;

namespace WhatsBuildingMcp.Tests;

public sealed class GeoUtilTests
{
    [Fact]
    public void Same_Point_Is_Zero()
    {
        Assert.Equal(0, GeoUtil.HaversineMeters(49.1659, -123.9401, 49.1659, -123.9401));
    }

    [Fact]
    public void Known_Nanaimo_Distance()
    {
        // Downtown Nanaimo to Departure Bay beach: roughly 4.7 km.
        var meters = GeoUtil.HaversineMeters(49.1659, -123.9401, 49.2040, -123.9709);

        Assert.InRange(meters, 4400, 5000);
    }

    [Fact]
    public void Is_Symmetric()
    {
        var forward = GeoUtil.HaversineMeters(49.1, -123.9, 49.2, -124.0);
        var backward = GeoUtil.HaversineMeters(49.2, -124.0, 49.1, -123.9);

        Assert.Equal(forward, backward, precision: 6);
    }

    [Theory]
    [InlineData("49.1659", 49.1659)]
    [InlineData("  -123.9401  ", -123.9401)]
    [InlineData("", null)]
    [InlineData("not-a-number", null)]
    public void Parses_Coordinates(string text, double? expected)
    {
        Assert.Equal(expected, GeoUtil.TryParseCoordinate(text));
    }
}
