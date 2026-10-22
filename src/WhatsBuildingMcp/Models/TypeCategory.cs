namespace WhatsBuildingMcp.Models;

/// <summary>Upstream application type categories (1 = Development, 2 = Permits, 3 = Subdivision/Other).</summary>
public static class TypeCategory
{
    public const int Development = 1;
    public const int BuildingPermits = 2;
    public const int SubdivisionOther = 3;

    public static string Name(int category) => category switch
    {
        Development => "Development Applications",
        BuildingPermits => "Building Permits",
        SubdivisionOther => "Subdivision/Other",
        _ => "Unknown",
    };

    /// <summary>Parses a category id or name fragment such as "2", "permit" or "subdivision".</summary>
    public static int? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        if (int.TryParse(text, out var id) && id is >= Development and <= SubdivisionOther)
        {
            return id;
        }

        if (text.Contains("develop", StringComparison.OrdinalIgnoreCase))
        {
            return Development;
        }

        if (text.Contains("permit", StringComparison.OrdinalIgnoreCase)
            || text.Contains("building", StringComparison.OrdinalIgnoreCase)
            || text.Contains("bp", StringComparison.OrdinalIgnoreCase))
        {
            return BuildingPermits;
        }

        if (text.Contains("subdiv", StringComparison.OrdinalIgnoreCase)
            || text.Contains("strata", StringComparison.OrdinalIgnoreCase)
            || text.Contains("other", StringComparison.OrdinalIgnoreCase))
        {
            return SubdivisionOther;
        }

        return null;
    }
}
