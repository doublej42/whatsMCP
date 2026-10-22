using System.ComponentModel;
using ModelContextProtocol.Server;
using WhatsBuildingMcp.Models;
using WhatsBuildingMcp.Services;

namespace WhatsBuildingMcp.Tools;

/// <summary>MCP tools exposing the City of Nanaimo What's Building public data (read-only).</summary>
[McpServerToolType]
public sealed class WhatsBuildingTools(
    ISnapshotService snapshots,
    IReferenceDataService referenceData,
    IDetailService details,
    IUpstreamContentService content)
{
    private readonly ISnapshotService _snapshots = snapshots;
    private readonly IReferenceDataService _referenceData = referenceData;
    private readonly IDetailService _details = details;
    private readonly IUpstreamContentService _content = content;

    [McpServerTool(Name = "search_applications", Title = "Search applications", ReadOnly = true, OpenWorld = true)]
    [Description("Searches City of Nanaimo What's Building development applications, building permits and subdivisions. "
        + "Filters are case-insensitive. Data is City of Nanaimo public data; it may lag the city's systems, "
        + "shows only active public applications by default, and is not legal advice.")]
    public async Task<string> SearchApplicationsAsync(
        [Description("Keyword matched against address, description, applicants and properties.")] string? keyword = null,
        [Description("File-number substring, e.g. 'DP001'.")] string? fileNumber = null,
        [Description("Neighbourhood name, e.g. 'Downtown/Old City'. See list_neighbourhoods.")] string? neighbourhood = null,
        [Description("Application type name, e.g. 'Development Permit'. See list_application_types.")] string? applicationType = null,
        [Description("Type category: 1/Development, 2/Building Permits, 3/Subdivision-Other.")] string? typeCategory = null,
        [Description("Status, e.g. 'ACTIVE'.")] string? status = null,
        [Description("Street name matched against civic address and properties.")] string? street = null,
        [Description("House number matched against civic address and properties.")] string? houseNumber = null,
        [Description("Bylaw text. Not available from the cache; ignored with a warning.")] string? bylaw = null,
        [Description("Submitted on/after this date (yyyy-MM-dd).")] string? submittedFrom = null,
        [Description("Submitted on/before this date (yyyy-MM-dd).")] string? submittedTo = null,
        [Description("Issued on/after this date (yyyy-MM-dd).")] string? issuedFrom = null,
        [Description("Issued on/before this date (yyyy-MM-dd).")] string? issuedTo = null,
        [Description("Minimum permit value.")] double? minPermitValue = null,
        [Description("Sort: area (default), type, folder, date, permitvalue, issueddate.")] string? sortBy = null,
        [Description("Sort descending.")] bool descending = false,
        [Description("Zero-based offset.")] int offset = 0,
        [Description("Results to return, 1-100 (default 25).")] int limit = ApplicationQuery.DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _snapshots.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var reference = await _referenceData.GetReferenceDataAsync(cancellationToken).ConfigureAwait(false);

        int? categoryId = null;
        if (!string.IsNullOrWhiteSpace(typeCategory))
        {
            categoryId = Models.TypeCategory.Parse(typeCategory);
            if (categoryId is null)
            {
                return McpJson.Serialize(new
                {
                    total = 0,
                    returned = 0,
                    offset,
                    snapshotAgeSeconds = SnapshotAge(snapshot),
                    items = Array.Empty<ApplicationSummary>(),
                    warning = $"Unknown type category '{typeCategory}'. Use 1 (Development), 2 (Building Permits) or 3 (Subdivision/Other).",
                });
            }
        }

        int? CategoryOf(string typeName)
            => reference.CategoryFor(typeName) ?? ReferenceDataFallback.CategoryForName(typeName);

        var (total, items, queryWarning) = ApplicationQuery.Apply(
            snapshot.Records,
            new ApplicationQueryParams(
                Keyword: InputValidation.CleanSearchText(keyword),
                FileNumber: InputValidation.CleanSearchText(fileNumber, 30),
                Neighbourhood: InputValidation.CleanSearchText(neighbourhood),
                ApplicationType: InputValidation.CleanSearchText(applicationType),
                TypeCategory: categoryId,
                Status: InputValidation.CleanSearchText(status, 30),
                Street: InputValidation.CleanSearchText(street),
                HouseNumber: InputValidation.CleanSearchText(houseNumber, 20),
                BylawRequested: !string.IsNullOrWhiteSpace(bylaw),
                SubmittedFrom: DateUtil.ParseDateParam(submittedFrom),
                SubmittedTo: DateUtil.ParseDateParam(submittedTo),
                IssuedFrom: DateUtil.ParseDateParam(issuedFrom),
                IssuedTo: DateUtil.ParseDateParam(issuedTo),
                MinPermitValue: minPermitValue.HasValue ? Convert.ToDecimal(minPermitValue.Value, DateUtil.EnUs) : null,
                SortBy: sortBy,
                Descending: descending,
                Offset: offset,
                Limit: limit),
            CategoryOf);

        var result = new PagedResult<ApplicationSummary>(
            total,
            items.Count,
            Math.Max(0, offset),
            SnapshotAge(snapshot),
            items.Select(r =>
            {
                var category = CategoryOf(r.ApplicationType);
                return ApplicationSummary.FromRecord(r, category.HasValue ? Models.TypeCategory.Name(category.Value) : null);
            }).ToArray(),
            JoinWarnings(snapshot.Warning, queryWarning));
        return McpJson.Serialize(result);
    }

    [McpServerTool(Name = "get_application", Title = "Get application detail", ReadOnly = true, OpenWorld = true)]
    [Description("Returns full detail for one application by file number (e.g. 'DP001002'), merging cached data "
        + "with the parsed public detail page: description, status dates, applicants, properties, attachments, "
        + "notices and links. Data is City of Nanaimo public data; it may lag the city's systems and is not legal advice.")]
    public async Task<string> GetApplicationAsync(
        [Description("File number, e.g. 'DP001002'.")] string fileNumber,
        CancellationToken cancellationToken = default)
    {
        if (!InputValidation.IsValidFileNumber(fileNumber))
        {
            return McpJson.Serialize(new { fileNumber, found = false, error = "Invalid file number. Use 3-30 letters, digits, '-' or '_'." });
        }

        var detail = await _details.GetApplicationAsync(fileNumber, cancellationToken).ConfigureAwait(false);
        if (detail is null)
        {
            return McpJson.Serialize(new { fileNumber, found = false, error = $"Application '{fileNumber}' was not found or is not public." });
        }

        return McpJson.Serialize(detail);
    }

    [McpServerTool(Name = "find_applications_near", Title = "Find applications near a point", ReadOnly = true, OpenWorld = true)]
    [Description("Finds active public applications within a radius of a latitude/longitude point, nearest first. "
        + "Data is City of Nanaimo public data; it may lag the city's systems and is not legal advice.")]
    public async Task<string> FindApplicationsNearAsync(
        [Description("Latitude in decimal degrees, e.g. 49.165.")] double latitude,
        [Description("Longitude in decimal degrees, e.g. -123.94.")] double longitude,
        [Description("Search radius in meters (1-50000).")] double radiusMeters = 1000,
        [Description("Results to return, 1-100 (default 25).")] int limit = ApplicationQuery.DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            return McpJson.Serialize(new { total = 0, returned = 0, offset = 0, snapshotAgeSeconds = 0L, items = Array.Empty<object>(), warning = "Invalid coordinates." });
        }

        var snapshot = await _snapshots.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var reference = await _referenceData.GetReferenceDataAsync(cancellationToken).ConfigureAwait(false);
        var radius = Math.Clamp(radiusMeters, 1, 50000);

        var matches = snapshot.Records
            .Where(r => r.Latitude.HasValue && r.Longitude.HasValue)
            .Select(r => (Record: r, Distance: GeoUtil.HaversineMeters(latitude, longitude, r.Latitude!.Value, r.Longitude!.Value)))
            .Where(entry => entry.Distance <= radius)
            .OrderBy(entry => entry.Distance)
            .ThenBy(entry => entry.Record.FileNumber, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var page = matches
            .Take(Math.Clamp(limit, 1, ApplicationQuery.MaxLimit))
            .Select(entry =>
            {
                var category = reference.CategoryFor(entry.Record.ApplicationType)
                    ?? ReferenceDataFallback.CategoryForName(entry.Record.ApplicationType);
                return new
                {
                    application = ApplicationSummary.FromRecord(
                        entry.Record,
                        category.HasValue ? Models.TypeCategory.Name(category.Value) : null),
                    distanceMeters = Math.Round(entry.Distance, 1),
                };
            })
            .ToArray();

        return McpJson.Serialize(new PagedResult<object>(matches.Length, page.Length, 0, SnapshotAge(snapshot), page, snapshot.Warning));
    }

    [McpServerTool(Name = "find_applications_by_address", Title = "Find applications by address", ReadOnly = true, OpenWorld = true)]
    [Description("Finds active public applications by street name with an optional house number. "
        + "Data is City of Nanaimo public data; it may lag the city's systems and is not legal advice.")]
    public async Task<string> FindApplicationsByAddressAsync(
        [Description("Street name, e.g. 'Fry Street'.")] string street,
        [Description("House number, e.g. '155'.")] string? houseNumber = null,
        [Description("Results to return, 1-100 (default 25).")] int limit = ApplicationQuery.DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _snapshots.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var reference = await _referenceData.GetReferenceDataAsync(cancellationToken).ConfigureAwait(false);

        int? CategoryOf(string typeName)
            => reference.CategoryFor(typeName) ?? ReferenceDataFallback.CategoryForName(typeName);

        var (total, items, _) = ApplicationQuery.Apply(
            snapshot.Records,
            new ApplicationQueryParams(
                Street: InputValidation.CleanSearchText(street),
                HouseNumber: InputValidation.CleanSearchText(houseNumber, 20),
                Limit: limit),
            CategoryOf);

        var result = new PagedResult<ApplicationSummary>(
            total,
            items.Count,
            0,
            SnapshotAge(snapshot),
            items.Select(r =>
            {
                var category = CategoryOf(r.ApplicationType);
                return ApplicationSummary.FromRecord(r, category.HasValue ? Models.TypeCategory.Name(category.Value) : null);
            }).ToArray(),
            snapshot.Warning);
        return McpJson.Serialize(result);
    }

    [McpServerTool(Name = "list_neighbourhoods", Title = "List neighbourhoods", ReadOnly = true, OpenWorld = true)]
    [Description("Lists Nanaimo neighbourhoods with active public application counts. "
        + "Data is City of Nanaimo public data; it may lag the city's systems and is not legal advice.")]
    public async Task<string> ListNeighbourhoodsAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _snapshots.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var reference = await _referenceData.GetReferenceDataAsync(cancellationToken).ConfigureAwait(false);

        var counts = snapshot.Records
            .GroupBy(r => r.Neighbourhood, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var names = reference.Areas.Select(a => a.Name)
            .Concat(counts.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return McpJson.Serialize(new
        {
            total = names.Length,
            snapshotAgeSeconds = SnapshotAge(snapshot),
            items = names.Select(n => new NeighbourhoodInfo(n, counts.TryGetValue(n, out var c) ? c : 0)).ToArray(),
            warning = snapshot.Warning,
        });
    }

    [McpServerTool(Name = "list_application_types", Title = "List application types", ReadOnly = true, OpenWorld = true)]
    [Description("Lists application types with their category and active public application counts. "
        + "Data is City of Nanaimo public data; it may lag the city's systems and is not legal advice.")]
    public async Task<string> ListApplicationTypesAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _snapshots.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var reference = await _referenceData.GetReferenceDataAsync(cancellationToken).ConfigureAwait(false);

        var counts = snapshot.Records
            .GroupBy(r => r.ApplicationType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var names = reference.Types.Select(t => t.Name)
            .Concat(counts.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return McpJson.Serialize(new
        {
            total = names.Length,
            snapshotAgeSeconds = SnapshotAge(snapshot),
            items = names.Select(n =>
            {
                var category = reference.CategoryFor(n) ?? ReferenceDataFallback.CategoryForName(n);
                return new ApplicationTypeInfo(n, category.HasValue ? Models.TypeCategory.Name(category.Value) : null, counts.TryGetValue(n, out var c) ? c : 0);
            }).ToArray(),
            warning = snapshot.Warning,
        });
    }

    [McpServerTool(Name = "get_pending_permits", Title = "Get pending permits", ReadOnly = true, OpenWorld = true)]
    [Description("Returns pending (not yet assigned) building permits by category: type, waiting count and next review date. "
        + "Data is City of Nanaimo public data; it may lag the city's systems and is not legal advice.")]
    public async Task<string> GetPendingPermitsAsync(CancellationToken cancellationToken = default)
    {
        var result = await _content.GetPendingPermitsAsync(cancellationToken).ConfigureAwait(false);
        return McpJson.Serialize(result);
    }

    [McpServerTool(Name = "get_recent_activity", Title = "Get recent activity", ReadOnly = true, OpenWorld = true)]
    [Description("Returns applications applied for, issued or completed in the last N days, newest first. "
        + "Combines cached snapshot dates with the upstream date-window search when available. "
        + "Data is City of Nanaimo public data; it may lag the city's systems, "
        + "shows only active public applications by default, and is not legal advice.")]
    public async Task<string> GetRecentActivityAsync(
        [Description("Look-back window in days, 1-90 (default 7).")] int days = 7,
        [Description("Activity kind: 'applied', 'issued' or 'completed'.")] string kind = "applied",
        [Description("Results to return, 1-100 (default 25).")] int limit = ApplicationQuery.DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var normalizedKind = (kind ?? "applied").Trim().ToLowerInvariant();
        if (normalizedKind is not ("applied" or "issued" or "completed"))
        {
            return McpJson.Serialize(new { kind, error = "Unknown kind. Use 'applied', 'issued' or 'completed'." });
        }

        var windowDays = Math.Clamp(days, 1, 90);
        var today = DateUtil.NanaimoToday();
        var snapshot = await _snapshots.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);

        var snapshotItems = ApplicationQuery.RecentFromSnapshot(snapshot.Records, normalizedKind, today, windowDays);
        var newStatus = normalizedKind switch
        {
            "applied" => "Applied for",
            "issued" => "Issued",
            _ => "Completed",
        };
        var markers = await _content.GetRecentMarkersAsync(newStatus, today.AddDays(-windowDays), today, cancellationToken).ConfigureAwait(false);
        var merged = ApplicationQuery.MergeRecent(snapshotItems, markers.Markers, normalizedKind, limit);

        var result = new RecentActivityResult(
            normalizedKind,
            windowDays,
            today.AddDays(-windowDays),
            today,
            snapshotItems.Count + markers.Markers.Count,
            merged.Count,
            merged,
            SnapshotAge(snapshot),
            JoinWarnings(snapshot.Warning, markers.Warning));
        return McpJson.Serialize(result);
    }

    [McpServerTool(Name = "get_statistics", Title = "Get statistics", ReadOnly = true, OpenWorld = true)]
    [Description("Counts active public applications and totals permit value, grouped by neighbourhood, type or status. "
        + "Data is City of Nanaimo public data; it may lag the city's systems, "
        + "shows only active public applications by default, and is not legal advice.")]
    public async Task<string> GetStatisticsAsync(
        [Description("Group by: 'neighbourhood', 'type' or 'status'.")] string groupBy = "neighbourhood",
        [Description("Filter: neighbourhood name.")] string? neighbourhood = null,
        [Description("Filter: application type name.")] string? applicationType = null,
        [Description("Filter: type category 1/2/3 or name.")] string? typeCategory = null,
        [Description("Filter: status, e.g. 'ACTIVE'.")] string? status = null,
        [Description("Filter: submitted on/after (yyyy-MM-dd).")] string? submittedFrom = null,
        [Description("Filter: submitted on/before (yyyy-MM-dd).")] string? submittedTo = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = (groupBy ?? "neighbourhood").Trim().ToLowerInvariant();
        if (normalized is not ("neighbourhood" or "area" or "type" or "applicationtype" or "status"))
        {
            return McpJson.Serialize(new { groupBy, error = "Unknown groupBy. Use 'neighbourhood', 'type' or 'status'." });
        }

        int? categoryId = null;
        if (!string.IsNullOrWhiteSpace(typeCategory))
        {
            categoryId = Models.TypeCategory.Parse(typeCategory);
            if (categoryId is null)
            {
                return McpJson.Serialize(new { groupBy, error = $"Unknown type category '{typeCategory}'. Use 1, 2 or 3." });
            }
        }

        var snapshot = await _snapshots.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var reference = await _referenceData.GetReferenceDataAsync(cancellationToken).ConfigureAwait(false);

        int? CategoryOf(string typeName)
            => reference.CategoryFor(typeName) ?? ReferenceDataFallback.CategoryForName(typeName);

        var parameters = new ApplicationQueryParams(
            Neighbourhood: InputValidation.CleanSearchText(neighbourhood),
            ApplicationType: InputValidation.CleanSearchText(applicationType),
            TypeCategory: categoryId,
            Status: InputValidation.CleanSearchText(status, 30),
            SubmittedFrom: DateUtil.ParseDateParam(submittedFrom),
            SubmittedTo: DateUtil.ParseDateParam(submittedTo));
        var matches = ApplicationQuery.Filter(snapshot.Records, parameters, CategoryOf);

        var result = new StatsResult(
            normalized,
            matches.Count,
            ApplicationQuery.GroupBy(matches, normalized),
            SnapshotAge(snapshot),
            snapshot.Warning);
        return McpJson.Serialize(result);
    }

    [McpServerTool(Name = "get_application_kml", Title = "Get application KML", ReadOnly = true, OpenWorld = true)]
    [Description("Returns the public KML (XML) geo document for one application by file number. "
        + "Data is City of Nanaimo public data; it may lag the city's systems and is not legal advice.")]
    public async Task<string> GetApplicationKmlAsync(
        [Description("File number, e.g. 'DP001002'.")] string fileNumber,
        CancellationToken cancellationToken = default)
    {
        if (!InputValidation.IsValidFileNumber(fileNumber))
        {
            return McpJson.Serialize(new { fileNumber, found = false, error = "Invalid file number. Use 3-30 letters, digits, '-' or '_'." });
        }

        var result = await _content.GetKmlAsync(fileNumber, cancellationToken).ConfigureAwait(false);
        return McpJson.Serialize(new
        {
            fileNumber,
            found = result.Kml is not null,
            kmlUrl = result.KmlUrl,
            kml = result.Kml,
            warning = result.Warning,
        });
    }

    private static long SnapshotAge(Snapshot snapshot)
        => (long)Math.Max(0, (DateTimeOffset.UtcNow - snapshot.FetchedUtc).TotalSeconds);

    private static string? JoinWarnings(string? first, string? second)
        => (first, second) switch
        {
            (null, null) => null,
            (null, _) => second,
            (_, null) => first,
            _ => first + " " + second,
        };
}
