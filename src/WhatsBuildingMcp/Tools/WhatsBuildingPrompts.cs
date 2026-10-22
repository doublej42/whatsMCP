using System.ComponentModel;
using ModelContextProtocol.Server;

namespace WhatsBuildingMcp.Tools;

/// <summary>MCP prompts for common What's Building questions.</summary>
[McpServerPromptType]
public sealed class WhatsBuildingPrompts
{
    [McpServerPrompt(Name = "summarize_neighbourhood_activity", Title = "Summarize development activity")]
    [Description("Summarizes recent development activity in a Nanaimo neighbourhood using the What's Building tools.")]
    public static string SummarizeNeighbourhoodActivity(
        [Description("Nanaimo neighbourhood name, e.g. 'Downtown/Old City'.")] string neighbourhood,
        [Description("Look-back window in days, 1-90 (default 30).")] int days = 30)
    {
        var window = Math.Clamp(days, 1, 90);
        return $"""
            Summarize development activity in the {neighbourhood} neighbourhood of Nanaimo, BC over the last {window} days.

            Use the search_applications tool with the neighbourhood filter to list current applications,
            get_recent_activity (kinds applied, issued and completed) for what changed recently, and
            get_application for detail on the most significant files. Group the summary by application
            type (development applications, building permits, subdivision/other), include file numbers,
            addresses, statuses and key dates, and call out anything with active public notices.

            Remember: this is City of Nanaimo public data; it may lag the city's systems, shows only
            active public applications by default, and is not legal advice.
            """;
    }
}
