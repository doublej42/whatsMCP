namespace WhatsBuildingMcp.Models;

/// <summary>Search-list entries for neighbourhoods and application types.</summary>
public sealed record NeighbourhoodInfo(string Name, int ActiveCount);

/// <summary>Search-list entries for neighbourhoods and application types.</summary>
public sealed record ApplicationTypeInfo(string Name, string? Category, int ActiveCount);
