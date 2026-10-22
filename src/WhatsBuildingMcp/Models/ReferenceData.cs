namespace WhatsBuildingMcp.Models;

/// <summary>Reference data scraped from the home-page search form (type/area ids and form defaults).</summary>
public sealed record ReferenceType(int TypeId, string Name, int Category);

/// <summary>Reference data scraped from the home-page search form.</summary>
public sealed record ReferenceArea(int AreaId, string Name);

/// <summary>One scraped search-form field (name/value pair, document order preserved).</summary>
public sealed record FormField(string Name, string? Value);

/// <summary>Scraped (live) or snapshot-derived (fallback) reference data.</summary>
public sealed record ReferenceData(
    IReadOnlyList<ReferenceType> Types,
    IReadOnlyList<ReferenceArea> Areas,
    IReadOnlyList<FormField> FormDefaults,
    bool IsLive,
    DateTimeOffset FetchedUtc)
{
    public int? CategoryFor(string applicationType)
    {
        foreach (var type in Types)
        {
            if (type.Name.Equals(applicationType, StringComparison.OrdinalIgnoreCase))
            {
                return type.Category;
            }
        }

        return null;
    }
}
