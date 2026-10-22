using WhatsBuildingMcp.Models;

namespace WhatsBuildingMcp.Services;

/// <summary>Search/filter/sort/paging parameters over the in-memory snapshot.</summary>
public sealed record ApplicationQueryParams(
    string? Keyword = null,
    string? FileNumber = null,
    string? Neighbourhood = null,
    string? ApplicationType = null,
    int? TypeCategory = null,
    string? Status = null,
    string? Street = null,
    string? HouseNumber = null,
    bool BylawRequested = false,
    DateTime? SubmittedFrom = null,
    DateTime? SubmittedTo = null,
    DateTime? IssuedFrom = null,
    DateTime? IssuedTo = null,
    decimal? MinPermitValue = null,
    string? SortBy = null,
    bool Descending = false,
    int Offset = 0,
    int Limit = 25);

/// <summary>Pure in-memory query logic over the snapshot (case-insensitive contains).</summary>
public static class ApplicationQuery
{
    public const int DefaultLimit = 25;
    public const int MaxLimit = 100;

    public static (long Total, IReadOnlyList<ApplicationRecord> Items, string? Warning) Apply(
        IEnumerable<ApplicationRecord> records,
        ApplicationQueryParams parameters,
        Func<string, int?> categoryOf)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var sorted = Filter(records, parameters, categoryOf);
        var offset = Math.Max(0, parameters.Offset);
        var limit = Math.Clamp(parameters.Limit, 1, MaxLimit);
        var page = sorted.Skip(offset).Take(limit).ToArray();

        string? warning = parameters.BylawRequested
            ? "Bylaw filtering is not available from the cached snapshot and was ignored."
            : null;
        return (sorted.Count, page, warning);
    }

    /// <summary>Filters and sorts without paging (used by statistics).</summary>
    public static IReadOnlyList<ApplicationRecord> Filter(
        IEnumerable<ApplicationRecord> records,
        ApplicationQueryParams parameters,
        Func<string, int?> categoryOf)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(categoryOf);

        var query = records;
        if (!string.IsNullOrWhiteSpace(parameters.Keyword))
        {
            var keyword = parameters.Keyword.Trim();
            query = query.Where(r => KeywordHaystack(r).Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(parameters.FileNumber))
        {
            var file = parameters.FileNumber.Trim();
            query = query.Where(r => r.FileNumber.Contains(file, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(parameters.Neighbourhood))
        {
            var area = parameters.Neighbourhood.Trim();
            query = query.Where(r => r.Neighbourhood.Contains(area, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(parameters.ApplicationType))
        {
            var type = parameters.ApplicationType.Trim();
            query = query.Where(r => r.ApplicationType.Contains(type, StringComparison.OrdinalIgnoreCase));
        }

        if (parameters.TypeCategory is { } category)
        {
            query = query.Where(r => categoryOf(r.ApplicationType) == category);
        }

        if (!string.IsNullOrWhiteSpace(parameters.Status))
        {
            var status = parameters.Status.Trim();
            query = query.Where(r => r.Status.Contains(status, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(parameters.Street) || !string.IsNullOrWhiteSpace(parameters.HouseNumber))
        {
            var street = parameters.Street?.Trim() ?? string.Empty;
            var house = parameters.HouseNumber?.Trim() ?? string.Empty;
            query = query.Where(r => AddressHaystack(r).Contains(street, StringComparison.OrdinalIgnoreCase)
                && AddressHaystack(r).Contains(house, StringComparison.OrdinalIgnoreCase));
        }

        if (parameters.SubmittedFrom.HasValue)
        {
            var from = parameters.SubmittedFrom.Value.Date;
            query = query.Where(r => r.DateSubmitted.HasValue && r.DateSubmitted.Value.Date >= from);
        }

        if (parameters.SubmittedTo.HasValue)
        {
            var to = parameters.SubmittedTo.Value.Date;
            query = query.Where(r => r.DateSubmitted.HasValue && r.DateSubmitted.Value.Date <= to);
        }

        if (parameters.IssuedFrom.HasValue)
        {
            var from = parameters.IssuedFrom.Value.Date;
            query = query.Where(r => r.IssuedDate.HasValue && r.IssuedDate.Value.Date >= from);
        }

        if (parameters.IssuedTo.HasValue)
        {
            var to = parameters.IssuedTo.Value.Date;
            query = query.Where(r => r.IssuedDate.HasValue && r.IssuedDate.Value.Date <= to);
        }

        if (parameters.MinPermitValue.HasValue)
        {
            var min = parameters.MinPermitValue.Value;
            query = query.Where(r => r.PermitValue.HasValue && r.PermitValue.Value >= min);
        }

        var filtered = query.ToArray();
        var sorted = Sort(filtered, parameters.SortBy, parameters.Descending);
        return sorted.ToArray();
    }

    public static IReadOnlyList<StatGroup> GroupBy(IEnumerable<ApplicationRecord> records, string groupBy)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupBy);

        Func<ApplicationRecord, string> key = groupBy.Trim().ToLowerInvariant() switch
        {
            "neighbourhood" or "area" => r => r.Neighbourhood,
            "type" or "applicationtype" => r => r.ApplicationType,
            "status" => r => r.Status,
            _ => throw new ArgumentException($"Unknown groupBy '{groupBy}'. Use neighbourhood, type or status.", nameof(groupBy)),
        };

        return records
            .GroupBy(key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new StatGroup(
                g.Key.Length == 0 ? "(blank)" : g.Key,
                g.Count(),
                g.Sum(r => r.PermitValue ?? 0m)))
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Snapshot-side recent activity: records whose applied/issued/final date falls in the window.</summary>
    public static IReadOnlyList<RecentActivityItem> RecentFromSnapshot(
        IEnumerable<ApplicationRecord> records,
        string kind,
        DateOnly today,
        int days)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);

        var start = today.AddDays(-days);
        var normalizedKind = kind.Trim().ToLowerInvariant();
        return records
            .Select(r => (Record: r, Date: RelevantDate(r, normalizedKind)))
            .Where(entry => entry.Date.HasValue
                && DateOnly.FromDateTime(entry.Date.Value.Date) >= start
                && DateOnly.FromDateTime(entry.Date.Value.Date) <= today)
            .OrderByDescending(entry => entry.Date)
            .ThenBy(entry => entry.Record.FileNumber, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new RecentActivityItem(
                entry.Record.FileNumber,
                entry.Record.CivicAddress,
                entry.Record.Neighbourhood.Length == 0 ? null : entry.Record.Neighbourhood,
                entry.Record.ApplicationType,
                entry.Record.Status,
                entry.Date,
                normalizedKind,
                "snapshot"))
            .ToArray();

        static DateTime? RelevantDate(ApplicationRecord record, string normalizedKind) => normalizedKind switch
        {
            "applied" => record.DateSubmitted,
            "issued" => record.IssuedDate,
            "completed" => record.FinalDate,
            _ => throw new ArgumentException($"Unknown kind '{normalizedKind}'. Use applied, issued or completed.", nameof(kind)),
        };
    }

    /// <summary>Merges snapshot hits with upstream-window markers (snapshot wins ties).</summary>
    public static IReadOnlyList<RecentActivityItem> MergeRecent(
        IReadOnlyList<RecentActivityItem> snapshotItems,
        IReadOnlyList<Marker> markers,
        string kind,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(snapshotItems);
        ArgumentNullException.ThrowIfNull(markers);

        var seen = new HashSet<string>(snapshotItems.Select(i => i.FileNumber), StringComparer.OrdinalIgnoreCase);
        var merged = new List<RecentActivityItem>(snapshotItems);
        foreach (var marker in markers)
        {
            if (marker.FileNumber.Length == 0 || !seen.Add(marker.FileNumber))
            {
                continue;
            }

            merged.Add(new RecentActivityItem(
                marker.FileNumber,
                TextUtil.Normalize(marker.CivicAddress),
                null,
                TextUtil.Normalize(marker.Type1),
                TextUtil.Normalize(marker.Status1),
                null,
                kind,
                "upstream-window"));
        }

        return merged.Take(Math.Clamp(limit, 1, MaxLimit)).ToArray();
    }

    private static IOrderedEnumerable<ApplicationRecord> Sort(ApplicationRecord[] records, string? sortBy, bool descending)
    {
        var key = (sortBy ?? "area").Trim().ToLowerInvariant();
        return (key, descending) switch
        {
            ("type", false) => records.OrderBy(r => r.ApplicationType, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.FileNumber, StringComparer.OrdinalIgnoreCase),
            ("type", true) => records.OrderByDescending(r => r.ApplicationType, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.FileNumber, StringComparer.OrdinalIgnoreCase),
            ("folder" or "filenumber" or "file", false) => records.OrderBy(r => r.FileNumber, StringComparer.OrdinalIgnoreCase),
            ("folder" or "filenumber" or "file", true) => records.OrderByDescending(r => r.FileNumber, StringComparer.OrdinalIgnoreCase),
            ("date" or "submitted" or "datesubmitted", false) => records.OrderBy(r => r.DateSubmitted).ThenBy(r => r.FileNumber, StringComparer.OrdinalIgnoreCase),
            ("date" or "submitted" or "datesubmitted", true) => records.OrderByDescending(r => r.DateSubmitted).ThenBy(r => r.FileNumber, StringComparer.OrdinalIgnoreCase),
            ("permitvalue" or "value", false) => records.OrderBy(r => r.PermitValue).ThenBy(r => r.FileNumber, StringComparer.OrdinalIgnoreCase),
            ("permitvalue" or "value", true) => records.OrderByDescending(r => r.PermitValue).ThenBy(r => r.FileNumber, StringComparer.OrdinalIgnoreCase),
            ("issued" or "issueddate", false) => records.OrderBy(r => r.IssuedDate).ThenBy(r => r.FileNumber, StringComparer.OrdinalIgnoreCase),
            ("issued" or "issueddate", true) => records.OrderByDescending(r => r.IssuedDate).ThenBy(r => r.FileNumber, StringComparer.OrdinalIgnoreCase),
            (_, true) => records.OrderByDescending(r => r.Neighbourhood, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.FileNumber, StringComparer.OrdinalIgnoreCase),
            _ => records.OrderBy(r => r.Neighbourhood, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.FileNumber, StringComparer.OrdinalIgnoreCase),
        };
    }

    private static string KeywordHaystack(ApplicationRecord record)
        => string.Join('\n', new[]
        {
            record.CivicAddress,
            record.Description,
            record.FileNumber,
            string.Join("; ", record.Properties),
            string.Join("; ", record.Applicants.Select(a => (a.Role is null ? a.Name : a.Role + ":" + a.Name))),
        });

    private static string AddressHaystack(ApplicationRecord record)
        => record.CivicAddress + "\n" + string.Join('\n', record.Properties);
}
