namespace WhatsBuildingMcp.Models;

/// <summary>Standard paged envelope returned by list-style tools.</summary>
public sealed record PagedResult<T>(
    long Total,
    int Returned,
    int Offset,
    long SnapshotAgeSeconds,
    IReadOnlyList<T> Items,
    string? Warning = null);
