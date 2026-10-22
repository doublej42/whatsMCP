using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using WhatsBuildingMcp.Models;

namespace WhatsBuildingMcp.Services;

/// <summary>Scrapes type/area reference data plus replayable form defaults from the home-page search form.</summary>
public static partial class ReferenceDataParser
{
    [GeneratedRegex(@"^SearchCriteria\.(?<cat>FolderTypeFilter|FolderTypeFilterBPs|FolderTypeFilterOthers)\.FolderTypes\[(?<index>\d+)\]\.FolderType\.(?<kind>TypeID|Type1)$", RegexOptions.IgnoreCase)]
    private static partial Regex TypeField();

    [GeneratedRegex(@"^SearchCriteria\.AreaFilters\[(?<index>\d+)\]\.Area\.AreaID$", RegexOptions.IgnoreCase)]
    private static partial Regex AreaIdField();

    public static ReferenceData Parse(string html, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var document = new HtmlParser().ParseDocument(html);
        var form = document.QuerySelector("form#FilterSubmitForm") as IHtmlFormElement
            ?? document.QuerySelector("form") as IHtmlFormElement;
        if (form is null)
        {
            logger?.LogWarning("Upstream search form not found");
            return new ReferenceData([], [], [], false, DateTimeOffset.UtcNow);
        }

        var fields = CollectFields(form);
        var types = CollectTypes(form);
        var areas = CollectAreas(form);
        return new ReferenceData(types, areas, fields, types.Count > 0 && areas.Count > 0, DateTimeOffset.UtcNow);
    }

    private static IReadOnlyList<FormField> CollectFields(IHtmlFormElement form)
    {
        var fields = new List<FormField>();
        foreach (var element in form.QuerySelectorAll("input, select, textarea"))
        {
            var name = element.GetAttribute("name");
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            if (element is IHtmlInputElement input)
            {
                var type = (input.Type ?? "text").ToLowerInvariant();
                if (type is "checkbox" or "radio")
                {
                    // Browsers only submit checked boxes; mirror that so our replay matches.
                    if (input.IsChecked)
                    {
                        fields.Add(new FormField(name, string.IsNullOrEmpty(input.Value) ? "on" : input.Value));
                    }
                }
                else if (type is not ("submit" or "button" or "reset" or "image" or "file"))
                {
                    fields.Add(new FormField(name, input.Value));
                }
            }
            else if (element is IHtmlSelectElement select)
            {
                fields.Add(new FormField(name, select.Value));
            }
            else if (element is IHtmlTextAreaElement area)
            {
                fields.Add(new FormField(name, area.Value));
            }
        }

        return fields;
    }

    private static IReadOnlyList<ReferenceType> CollectTypes(IHtmlFormElement form)
    {
        var slots = new Dictionary<string, (int Category, int TypeId, string? Name)>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in form.QuerySelectorAll("input").OfType<IHtmlInputElement>())
        {
            var match = TypeField().Match(input.Name ?? string.Empty);
            if (!match.Success)
            {
                continue;
            }

            var key = match.Groups["cat"].Value + ":" + match.Groups["index"].Value;
            slots.TryGetValue(key, out var slot);
            slot.Category = match.Groups["cat"].Value switch
            {
                "FolderTypeFilter" => Models.TypeCategory.Development,
                "FolderTypeFilterBPs" => Models.TypeCategory.BuildingPermits,
                _ => Models.TypeCategory.SubdivisionOther,
            };

            if (match.Groups["kind"].Value.Equals("TypeID", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(input.Value, out var typeId))
                {
                    slot.TypeId = typeId;
                }
            }
            else if (!string.IsNullOrWhiteSpace(input.Value))
            {
                slot.Name = input.Value.Trim();
            }

            slots[key] = slot;
        }

        return slots.Values
            .Where(slot => slot.TypeId > 0)
            .Select(slot => new ReferenceType(
                slot.TypeId,
                slot.Name ?? "Type " + slot.TypeId.ToString(DateUtil.EnUs),
                slot.Category))
            .OrderBy(t => t.Category)
            .ThenBy(t => t.TypeId)
            .ToArray();
    }

    private static IReadOnlyList<ReferenceArea> CollectAreas(IHtmlFormElement form)
    {
        var result = new List<ReferenceArea>();
        foreach (var input in form.QuerySelectorAll("input").OfType<IHtmlInputElement>())
        {
            var match = AreaIdField().Match(input.Name ?? string.Empty);
            if (!match.Success || !int.TryParse(input.Value, out var areaId))
            {
                continue;
            }

            var name = AreaNameFor(form, match.Groups["index"].Value) ?? "Area " + areaId.ToString(DateUtil.EnUs);
            result.Add(new ReferenceArea(areaId, name));
        }

        return result.OrderBy(a => a.AreaId).ToArray();
    }

    private static string? AreaNameFor(IHtmlFormElement form, string index)
    {
        var checkbox = form.QuerySelector($"input[name='SearchCriteria.AreaFilters[{index}].Checked']");
        var label = checkbox?.Closest("label");
        var name = TextUtil.NullIfEmpty(label?.TextContent);
        return name;
    }
}
