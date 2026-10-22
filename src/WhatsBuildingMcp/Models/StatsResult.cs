namespace WhatsBuildingMcp.Models;

/// <summary>One statistics bucket.</summary>
public sealed record StatGroup(string Key, int Count, decimal TotalPermitValue);

/// <summary>Envelope returned by <c>get_statistics</c>.</summary>
public sealed record StatsResult(
    string GroupBy,
    long Total,
    IReadOnlyList<StatGroup> Groups,
    long SnapshotAgeSeconds,
    string? Warning = null);
