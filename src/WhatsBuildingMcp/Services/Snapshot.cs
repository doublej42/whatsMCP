using WhatsBuildingMcp.Models;

namespace WhatsBuildingMcp.Services;

/// <summary>Immutable CSV snapshot plus its fetch metadata.</summary>
public sealed record Snapshot(
    IReadOnlyList<ApplicationRecord> Records,
    DateTimeOffset FetchedUtc,
    bool IsStale,
    string? Warning);
