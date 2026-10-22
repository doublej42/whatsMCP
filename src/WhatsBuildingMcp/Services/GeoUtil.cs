using System.Globalization;

namespace WhatsBuildingMcp.Services;

/// <summary>Geographic helpers (haversine distance, coordinate parsing).</summary>
public static class GeoUtil
{
    public const double EarthRadiusMeters = 6371000.0;

    public static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2))
            * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1.0, Math.Sqrt(a)));

        static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
    }

    public static double? TryParseCoordinate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}
