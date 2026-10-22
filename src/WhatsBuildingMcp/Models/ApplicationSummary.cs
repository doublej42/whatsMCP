namespace WhatsBuildingMcp.Models;

/// <summary>Compact per-application summary used in search results.</summary>
public sealed record ApplicationSummary(
    string FileNumber,
    string CivicAddress,
    string Neighbourhood,
    string ApplicationType,
    string? TypeCategory,
    string Status,
    DateTime? DateSubmitted,
    DateTime? IssuedDate,
    decimal? PermitValue,
    double? Latitude,
    double? Longitude,
    string Url,
    string? DescriptionSnippet)
{
    public static ApplicationSummary FromRecord(ApplicationRecord record, string? category)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new ApplicationSummary(
            record.FileNumber,
            record.CivicAddress,
            record.Neighbourhood,
            record.ApplicationType,
            category,
            record.Status,
            record.DateSubmitted,
            record.IssuedDate,
            record.PermitValue,
            record.Latitude,
            record.Longitude,
            record.Url,
            Snippet(record.Description));

        static string? Snippet(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                return null;
            }

            var text = description.Trim();
            return text.Length <= 200 ? text : text.Substring(0, 200).TrimEnd() + "…";
        }
    }
}
