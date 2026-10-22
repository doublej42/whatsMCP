using WhatsBuildingMcp.Models;

namespace WhatsBuildingMcp.Services;

/// <summary>
/// Built-in copy of the upstream type/area reference data (captured from the live
/// search form), used only when the live form cannot be scraped.
/// </summary>
public static class ReferenceDataFallback
{
    private static readonly (int TypeId, string Name, int Category)[] Types =
    [
        (11, "Board of Variance", TypeCategory.Development),
        (45, "Covenant Amendment", TypeCategory.Development),
        (1, "Development Permit", TypeCategory.Development),
        (4, "Development Variance Permit", TypeCategory.Development),
        (47, "Land Reserve", TypeCategory.Development),
        (5, "Land Use Contract or Covenant Amendment", TypeCategory.Development),
        (8, "Liquor Licence Application", TypeCategory.Development),
        (15, "Official Community Plan Amendment", TypeCategory.Development),
        (2, "Rezoning Application", TypeCategory.Development),
        (6, "Temporary Use Permit", TypeCategory.Development),
        (32, "Access Permit", TypeCategory.BuildingPermits),
        (22, "Accessory Building", TypeCategory.BuildingPermits),
        (36, "Building Envelope Repair", TypeCategory.BuildingPermits),
        (18, "Commercial / Multi / Industrial", TypeCategory.BuildingPermits),
        (29, "Commercial / Multi-Res Alteration", TypeCategory.BuildingPermits),
        (33, "Demolition Permit", TypeCategory.BuildingPermits),
        (34, "Fire Damage", TypeCategory.BuildingPermits),
        (25, "Garage / Carport Permit", TypeCategory.BuildingPermits),
        (31, "Grow Op Damage Permit", TypeCategory.BuildingPermits),
        (56, "Historic Permit", TypeCategory.BuildingPermits),
        (19, "Leasehold Improvements", TypeCategory.BuildingPermits),
        (37, "Locate Permit", TypeCategory.BuildingPermits),
        (57, "Non-Dwelling Accessory Building", TypeCategory.BuildingPermits),
        (26, "On-site Servicing Permit", TypeCategory.BuildingPermits),
        (28, "Retaining Wall", TypeCategory.BuildingPermits),
        (24, "Service Permit", TypeCategory.BuildingPermits),
        (30, "Sign Permit", TypeCategory.BuildingPermits),
        (20, "Single / Two Family Alteration", TypeCategory.BuildingPermits),
        (21, "Single / Two Family Dwelling", TypeCategory.BuildingPermits),
        (40, "Sprinkler Permit", TypeCategory.BuildingPermits),
        (35, "Swimming Pool", TypeCategory.BuildingPermits),
        (27, "Temporary Location", TypeCategory.BuildingPermits),
        (42, "Phased Strata", TypeCategory.SubdivisionOther),
        (43, "Strata Conversion", TypeCategory.SubdivisionOther),
        (41, "Subdivision", TypeCategory.SubdivisionOther),
        (44, "Subdivision - Other", TypeCategory.SubdivisionOther),
    ];

    private static readonly (int AreaId, string Name)[] Areas =
    [
        (8, "Chase River"),
        (3, "Departure Bay"),
        (13, "Diver Lake"),
        (11, "Dover"),
        (2, "Downtown/Old City"),
        (9, "Duke Point"),
        (15, "Hammond Bay"),
        (10, "Harewood"),
        (14, "Linley Valley"),
        (16, "Newcastle/Brechin"),
        (7, "North Slope"),
        (5, "Northfield"),
        (20, "Pleasant Valley"),
        (4, "Protection Island"),
        (12, "Rutherford"),
        (6, "South End"),
        (17, "Townsite"),
        (19, "University District"),
        (18, "Westwood"),
    ];

    public static IReadOnlyList<ReferenceType> AllTypes()
        => Types.Select(t => new ReferenceType(t.TypeId, t.Name, t.Category)).ToArray();

    public static IReadOnlyList<ReferenceArea> AllAreas()
        => Areas.Select(a => new ReferenceArea(a.AreaId, a.Name)).ToArray();

    public static int? CategoryForName(string applicationType)
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
