namespace WhatsBuildingMcp.Models;

/// <summary>One <c>/GetMarkers</c> JSON entry.</summary>
public sealed record Marker(
    int ID = 0,
    string FileNumber = "",
    string Lat = "",
    string Long = "",
    string Description = "",
    string CivicAddress = "",
    string Type1 = "",
    string Status1 = "",
    string MapIconURL = "");
