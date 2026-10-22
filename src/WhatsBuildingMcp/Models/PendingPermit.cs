namespace WhatsBuildingMcp.Models;

/// <summary>One row of the <c>/PendingPermits</c> table.</summary>
public sealed record PendingPermit(string ApplicationType, int WaitingCount, DateTime? NextUpForReview);

/// <summary>Envelope returned by <c>get_pending_permits</c>.</summary>
public sealed record PendingPermitsResult(
    IReadOnlyList<PendingPermit> Items,
    DateTimeOffset FetchedUtc,
    string? Warning = null);
