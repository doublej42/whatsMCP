namespace WhatsBuildingMcp.Models;

/// <summary>One recent-activity hit. <c>Source</c> is "snapshot" or "upstream-window".</summary>
public sealed record RecentActivityItem(
    string FileNumber,
    string CivicAddress,
    string? Neighbourhood,
    string ApplicationType,
    string Status,
    DateTime? RelevantDate,
    string Kind,
    string Source);

/// <summary>Envelope returned by <c>get_recent_activity</c>.</summary>
public sealed record RecentActivityResult(
    string Kind,
    int Days,
    DateOnly WindowStart,
    DateOnly WindowEnd,
    long Total,
    int Returned,
    IReadOnlyList<RecentActivityItem> Items,
    long SnapshotAgeSeconds,
    string? Warning = null);
