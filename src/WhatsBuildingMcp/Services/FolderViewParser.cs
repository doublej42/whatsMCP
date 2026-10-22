using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using WhatsBuildingMcp.Models;

namespace WhatsBuildingMcp.Services;

/// <summary>AngleSharp parser for <c>/FolderView/{fileNumber}</c> HTML fragments.</summary>
public static class FolderViewParser
{
    private const string NotFoundMarker = "The application was not found";

    public static FolderViewDetail Parse(string html, string fileNumber, Uri baseUri)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileNumber);
        ArgumentNullException.ThrowIfNull(baseUri);

        var document = new HtmlParser().ParseDocument(html);
        if (document.Body is not null
            && document.Body.TextContent.Contains(NotFoundMarker, StringComparison.OrdinalIgnoreCase))
        {
            return FolderViewDetail.NotFound(fileNumber);
        }

        var main = FindMainTable(document);
        var rows = main is null ? new Dictionary<string, IElement>(StringComparer.OrdinalIgnoreCase) : LabeledRows(main);

        return new FolderViewDetail(
            Found: true,
            FileNumber: CellText(rows, "File Number") is { Length: > 0 } fn ? fn : fileNumber,
            CivicAddress: CellText(rows, "Street Address"),
            Neighbourhood: CellText(rows, "Neighbourhood"),
            ApplicationType: CellText(rows, "Type of Application"),
            DateSubmitted: DateUtil.ParseUpstreamDate(CellText(rows, "Date Application Submitted")),
            StaffContact: StaffContact(rows),
            Description: CellText(rows, "Application Description"),
            Status: CellText(rows, "Application Status"),
            IssuedDate: DateUtil.ParseUpstreamDate(CellText(rows, "Issued Date")),
            CloseDate: DateUtil.ParseUpstreamDate(CellText(rows, "File Close Date")),
            ExpiryDate: DateUtil.ParseUpstreamDate(CellText(rows, "Expiry Date")),
            Properties: ParseProperties(document, baseUri),
            Attachments: ParseAttachments(document, baseUri),
            Notices: ParseNotices(document, baseUri),
            EducationalNote: ParseEducationalNote(document),
            ProcessSteps: ParseProcessSteps(document),
            Policies: ParsePolicies(document, baseUri),
            PublicUrl: PublicUrl(document),
            Latitude: GeoUtil.TryParseCoordinate(document.QuerySelector("#hdnLat")?.GetAttribute("value")),
            Longitude: GeoUtil.TryParseCoordinate(document.QuerySelector("#hdnLng")?.GetAttribute("value")));
    }

    private static IHtmlTableElement? FindMainTable(IDocument document)
    {
        // The main detail table is the first standard table headed by #spanFileNumber.
        foreach (var table in document.QuerySelectorAll("table.standard-table"))
        {
            if (table.QuerySelector("#spanFileNumber") is not null)
            {
                return table as IHtmlTableElement;
            }
        }

        return document.QuerySelector("table.standard-table") as IHtmlTableElement;
    }

    private static Dictionary<string, IElement> LabeledRows(IHtmlTableElement table)
    {
        var rows = new Dictionary<string, IElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in table.Rows)
        {
            if (row.Cells.Length != 2 || row.Cells[0] is not IHtmlTableCellElement label)
            {
                continue;
            }

            var key = TextUtil.Normalize(label.TextContent).TrimEnd(':').Trim();
            if (key.Length > 0 && !rows.ContainsKey(key))
            {
                rows[key] = row.Cells[1];
            }
        }

        return rows;
    }

    private static string? CellText(Dictionary<string, IElement> rows, string label)
        => rows.TryGetValue(label, out var cell) ? TextUtil.NullIfEmpty(cell.TextContent) : null;

    private static string? StaffContact(Dictionary<string, IElement> rows)
    {
        if (!rows.TryGetValue("Staff Contact", out var cell))
        {
            return null;
        }

        // Name, phone and email are separated by <br/> elements, which TextContent drops.
        var parts = new List<string>();
        foreach (var node in cell.ChildNodes)
        {
            var text = node.TextContent;
            if (!string.IsNullOrWhiteSpace(text))
            {
                parts.Add(TextUtil.Normalize(text));
            }
        }

        return parts.Count == 0 ? null : string.Join('\n', parts);
    }

    private static IElement? TableAfterHeading(IDocument document, string heading)
    {
        foreach (var header in document.QuerySelectorAll("h2, h3"))
        {
            if (TextUtil.Normalize(header.TextContent).Equals(heading, StringComparison.OrdinalIgnoreCase))
            {
                var table = header.NextElementSibling;
                while (table is not null && !table.TagName.Equals("TABLE", StringComparison.OrdinalIgnoreCase))
                {
                    table = table.NextElementSibling;
                }

                return table;
            }
        }

        return null;
    }

    private static IReadOnlyList<DetailProperty> ParseProperties(IDocument document, Uri baseUri)
    {
        var table = TableAfterHeading(document, "Associated Properties");
        var result = new List<DetailProperty>();
        if (table is null)
        {
            return result;
        }

        foreach (var row in table.QuerySelectorAll("tr"))
        {
            var cells = row.QuerySelectorAll("td").ToArray();
            if (cells.Length < 3)
            {
                continue;
            }

            var zoneLink = cells[1].QuerySelector("a");
            var moreLink = cells[2].QuerySelector("a");
            result.Add(new DetailProperty(
                Address: TextUtil.Normalize(cells[0].TextContent),
                Zone: TextUtil.NullIfEmpty(cells[1].TextContent),
                ZoneUrl: ResolveUrl(baseUri, zoneLink?.GetAttribute("href")),
                MoreText: TextUtil.NullIfEmpty(moreLink?.TextContent),
                MoreUrl: ResolveUrl(baseUri, moreLink?.GetAttribute("href"))));
        }

        return result;
    }

    private static IReadOnlyList<DetailAttachment> ParseAttachments(IDocument document, Uri baseUri)
    {
        var result = new List<DetailAttachment>();
        foreach (var table in document.QuerySelectorAll("table.standard-table"))
        {
            var heading = table.QuerySelector("th");
            if (heading is null || !TextUtil.Normalize(heading.TextContent).Contains("Attachments", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var row in table.QuerySelectorAll("tr"))
            {
                var link = row.QuerySelector("td a[href]");
                if (link is null)
                {
                    continue;
                }

                var url = ResolveUrl(baseUri, link.GetAttribute("href"));
                if (url is null)
                {
                    continue;
                }

                var posted = null as DateTime?;
                foreach (var italic in row.QuerySelectorAll("i"))
                {
                    var text = TextUtil.Normalize(italic.TextContent);
                    const string prefix = "Posted:";
                    if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        posted = DateUtil.ParseUpstreamDate(text.Substring(prefix.Length).Trim());
                    }
                }

                result.Add(new DetailAttachment(
                    Name: TextUtil.Normalize(link.TextContent),
                    Url: url,
                    PostedDate: posted));
            }
        }

        return result;
    }

    private static IReadOnlyList<DetailNotice> ParseNotices(IDocument document, Uri baseUri)
    {
        var result = new List<DetailNotice>();
        foreach (var header in document.QuerySelectorAll("h2, h3"))
        {
            if (!TextUtil.Normalize(header.TextContent).Contains("Notices", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var sibling = header.NextElementSibling;
            while (sibling is not null && sibling.TagName.Equals("TABLE", StringComparison.OrdinalIgnoreCase))
            {
                if (!LooksLikeNoticeTable(sibling))
                {
                    break;
                }

                result.Add(ParseNoticeTable(sibling, baseUri));
                sibling = sibling.NextElementSibling;
            }
        }

        return result;
    }

    private static bool LooksLikeNoticeTable(IElement table)
    {
        // Notice tables are titled ("Council Meeting", ...) and carry Location/Date/Time rows;
        // the Map table that often follows them has neither.
        if (table.QuerySelector("th b") is not null)
        {
            return true;
        }

        foreach (var row in table.QuerySelectorAll("tr"))
        {
            var cells = row.QuerySelectorAll("td").ToArray();
            if (cells.Length == 2)
            {
                var label = TextUtil.Normalize(cells[0].TextContent).TrimEnd(':').Trim();
                if (label.Equals("Location", StringComparison.OrdinalIgnoreCase)
                    || label.Equals("Date", StringComparison.OrdinalIgnoreCase)
                    || label.Equals("Time", StringComparison.OrdinalIgnoreCase)
                    || label.Equals("Posted", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static DetailNotice ParseNoticeTable(IElement table, Uri baseUri)
    {
        var title = TextUtil.Normalize(table.QuerySelector("th")?.TextContent ?? "Notice");
        string? location = null;
        string? date = null;
        string? time = null;
        DateTime? posted = null;
        var details = new List<string>();
        var links = new List<DetailLink>();

        foreach (var row in table.QuerySelectorAll("tr"))
        {
            var cells = row.QuerySelectorAll("td").ToArray();
            if (cells.Length == 2)
            {
                var label = TextUtil.Normalize(cells[0].TextContent).TrimEnd(':').Trim();
                var value = TextUtil.NullIfEmpty(cells[1].TextContent);
                if (label.Equals("Location", StringComparison.OrdinalIgnoreCase))
                {
                    location = value;
                }
                else if (label.Equals("Date", StringComparison.OrdinalIgnoreCase))
                {
                    date = value;
                }
                else if (label.Equals("Time", StringComparison.OrdinalIgnoreCase))
                {
                    time = value;
                }
                else if (label.Equals("Posted", StringComparison.OrdinalIgnoreCase))
                {
                    posted = DateUtil.ParseUpstreamDate(value);
                }
                else if (value is not null)
                {
                    details.Add(label.Length == 0 ? value : label + ": " + value);
                }
            }
            else if (cells.Length == 1)
            {
                var text = TextUtil.NullIfEmpty(cells[0].TextContent);
                if (text is not null && cells[0].QuerySelector("a[href]") is null)
                {
                    details.Add(text);
                }
            }

            foreach (var link in row.QuerySelectorAll("a[href]"))
            {
                var url = ResolveUrl(baseUri, link.GetAttribute("href"));
                if (url is not null)
                {
                    links.Add(new DetailLink(TextUtil.Normalize(link.TextContent), url));
                }
            }
        }

        return new DetailNotice(
            Title: title.Length == 0 ? "Notice" : title,
            Location: location,
            Date: date,
            Time: time,
            Details: details.Count == 0 ? null : string.Join('\n', details),
            Links: links,
            PostedDate: posted);
    }

    private static string? ParseEducationalNote(IDocument document)
    {
        // Opportunistic: no educational-note section has been observed in FolderView
        // output so far; capture it if the upstream ever renders one.
        foreach (var header in document.QuerySelectorAll("h2, h3, div"))
        {
            if (!IsEducationalHeader(header))
            {
                continue;
            }

            var sibling = header.NextElementSibling;
            while (sibling is not null)
            {
                var text = TextUtil.NullIfEmpty(sibling.TextContent);
                if (text is not null)
                {
                    return text;
                }

                sibling = sibling.NextElementSibling;
            }
        }

        return null;

        static bool IsEducationalHeader(IElement header)
        {
            if (header.TagName.Equals("H2", StringComparison.OrdinalIgnoreCase)
                || header.TagName.Equals("H3", StringComparison.OrdinalIgnoreCase))
            {
                return TextUtil.Normalize(header.TextContent).Contains("educat", StringComparison.OrdinalIgnoreCase);
            }

            return (header.GetAttribute("class")?.Contains("educat", StringComparison.OrdinalIgnoreCase) ?? false)
                || (header.GetAttribute("id")?.Contains("educat", StringComparison.OrdinalIgnoreCase) ?? false);
        }
    }

    private static IReadOnlyList<ProcessStep> ParseProcessSteps(IDocument document)
    {
        var table = TableAfterHeading(document, "Process Steps");
        var result = new List<ProcessStep>();
        if (table is null)
        {
            return result;
        }

        foreach (var row in table.QuerySelectorAll("tr"))
        {
            var cells = row.QuerySelectorAll("td").ToArray();
            if (cells.Length != 2)
            {
                continue;
            }

            var task = TextUtil.NullIfEmpty(cells[0].ChildNodes.FirstOrDefault(n => n.NodeType == NodeType.Text)?.TextContent)
                ?? TextUtil.Normalize(cells[0].TextContent);
            var detail = TextUtil.NullIfEmpty(cells[0].QuerySelector("p")?.TextContent);
            result.Add(new ProcessStep(
                Task: task,
                Detail: detail,
                Status: TextUtil.NullIfEmpty(cells[1].TextContent)));
        }

        return result;
    }

    private static IReadOnlyList<RelatedPolicy> ParsePolicies(IDocument document, Uri baseUri)
    {
        var result = new List<RelatedPolicy>();
        foreach (var table in document.QuerySelectorAll("table.standard-table"))
        {
            var heading = table.QuerySelector("th");
            if (heading is null || !TextUtil.Normalize(heading.TextContent).Contains("Policies and Regulations", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var link in table.QuerySelectorAll("td a[href]"))
            {
                var url = ResolveUrl(baseUri, link.GetAttribute("href"));
                var title = TextUtil.Normalize(link.TextContent);
                if (url is not null && title.Length > 0)
                {
                    result.Add(new RelatedPolicy(title, url));
                }
            }
        }

        return result;
    }

    private static string? PublicUrl(IDocument document)
        => TextUtil.NullIfEmpty(document.QuerySelector("#txtEmailLink")?.GetAttribute("value"));

    private static string? ResolveUrl(Uri baseUri, string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        var value = href.Trim();
        if (value.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Uri.TryCreate(baseUri, value, out var resolved) ? resolved.AbsoluteUri : null;
    }
}
