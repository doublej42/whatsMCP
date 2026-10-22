namespace WhatsBuildingMcp.Models;

/// <summary>An applicant on an application, as <c>Role:Name</c> in the CSV.</summary>
public sealed record Applicant(string? Role, string Name);
