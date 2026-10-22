using AngleSharp.Html.Parser;
using WhatsBuildingMcp.Models;

namespace WhatsBuildingMcp.Services;

/// <summary>Scraper for the <c>/PendingPermits</c> HTML table.</summary>
public static class PendingPermitsParser
{
    public static IReadOnlyList<PendingPermit> Parse(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var document = new HtmlParser().ParseDocument(html);
        var result = new List<PendingPermit>();

        foreach (var table in document.QuerySelectorAll("table"))
        {
            var headerCells = table.QuerySelectorAll("tr").FirstOrDefault()?.QuerySelectorAll("th, td");
            if (headerCells is null || !IsPendingTable(headerCells))
            {
                continue;
            }

            foreach (var row in table.QuerySelectorAll("tr").Skip(1))
            {
                var cells = row.QuerySelectorAll("td").ToArray();
                if (cells.Length < 3)
                {
                    continue;
                }

                var type = TextUtil.Normalize(cells[0].TextContent);
                if (type.Length == 0)
                {
                    continue;
                }

                result.Add(new PendingPermit(
                    ApplicationType: type,
                    WaitingCount: int.TryParse(TextUtil.Normalize(cells[1].TextContent), out var count) ? count : 0,
                    NextUpForReview: DateUtil.ParseUpstreamDate(cells[2].TextContent)));
            }
        }

        return result;

        static bool IsPendingTable(AngleSharp.Dom.IHtmlCollection<AngleSharp.Dom.IElement> headerCells)
        {
            var header = string.Join(" | ", headerCells.Select(c => c.TextContent));
            return header.Contains("Application Type", StringComparison.OrdinalIgnoreCase)
                && header.Contains("Waiting", StringComparison.OrdinalIgnoreCase);
        }
    }
}
