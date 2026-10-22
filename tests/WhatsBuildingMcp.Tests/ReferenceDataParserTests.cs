using WhatsBuildingMcp.Models;
using WhatsBuildingMcp.Services;

namespace WhatsBuildingMcp.Tests;

public sealed class ReferenceDataParserTests
{
    [Fact]
    public void Parses_Types_With_Categories()
    {
        var html = File.ReadAllText(Path.Combine("Fixtures", "searchform_fragment.html"));

        var data = ReferenceDataParser.Parse(html);

        Assert.True(data.IsLive);
        Assert.Contains(data.Types, t => t.TypeId == 1 && t.Name == "Development Permit" && t.Category == TypeCategory.Development);
        Assert.Contains(data.Types, t => t.TypeId == 11 && t.Name == "Board of Variance" && t.Category == TypeCategory.Development);
        Assert.Contains(data.Types, t => t.TypeId == 24 && t.Name == "Service Permit" && t.Category == TypeCategory.BuildingPermits);
        Assert.Contains(data.Types, t => t.TypeId == 21 && t.Name == "Single / Two Family Dwelling" && t.Category == TypeCategory.BuildingPermits);
        Assert.Contains(data.Types, t => t.TypeId == 41 && t.Name == "Subdivision" && t.Category == TypeCategory.SubdivisionOther);
        Assert.Contains(data.Types, t => t.TypeId == 44 && t.Name == "Subdivision - Other" && t.Category == TypeCategory.SubdivisionOther);
    }

    [Fact]
    public void Parses_Areas_With_Names()
    {
        var html = File.ReadAllText(Path.Combine("Fixtures", "searchform_fragment.html"));

        var data = ReferenceDataParser.Parse(html);

        Assert.Contains(data.Areas, a => a.AreaId == 8 && a.Name == "Chase River");
        Assert.Contains(data.Areas, a => a.AreaId == 2 && a.Name == "Downtown/Old City");
        Assert.Contains(data.Areas, a => a.AreaId == 5 && a.Name == "Northfield");
    }

    [Fact]
    public void Collects_Replayable_Form_Fields()
    {
        var html = File.ReadAllText(Path.Combine("Fixtures", "searchform_fragment.html"));

        var data = ReferenceDataParser.Parse(html);

        // Hidden IDs ride along for the model binder.
        Assert.Contains(data.FormDefaults, f => f.Name == "SearchCriteria.FolderTypeFilter.FolderTypes[2].FolderType.TypeID" && f.Value == "1");
        Assert.Contains(data.FormDefaults, f => f.Name == "SearchCriteria.AreaFilters[0].Area.AreaID" && f.Value == "8");

        // Checked boxes post their value; the unchecked ShowInactive box stays out
        // (its hidden "false" twin is included instead).
        Assert.Contains(data.FormDefaults, f => f.Name == "SearchCriteria.AreaFilters[0].Checked" && f.Value == "true");
        Assert.DoesNotContain(data.FormDefaults, f => f.Name == "SearchCriteria.ShowInactiveFolders" && f.Value == "true");
        Assert.Contains(data.FormDefaults, f => f.Name == "SearchCriteria.ShowInactiveFolders" && f.Value == "false");

        Assert.Contains(data.FormDefaults, f => f.Name == "SearchCriteria.StatusStartDate");
        Assert.Contains(data.FormDefaults, f => f.Name == "UserView.Sort");
    }
}
