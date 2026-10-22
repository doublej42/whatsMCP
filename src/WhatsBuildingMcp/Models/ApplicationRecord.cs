namespace WhatsBuildingMcp.Models;

/// <summary>One row of <c>whatsbuilding.csv</c> (a <c>VmFolderDetails</c>).</summary>
public sealed record ApplicationRecord(
    string FileNumber,
    string Url,
    DateTime? DateSubmitted,
    string CivicAddress,
    string Neighbourhood,
    string ApplicationType,
    string StaffContact,
    string Description,
    string Status,
    decimal? PermitValue,
    double? BuildingArea,
    IReadOnlyList<Applicant> Applicants,
    DateTime? IssuedDate,
    DateTime? FinalDate,
    DateTime? ExpiryDate,
    IReadOnlyList<string> Properties,
    bool HasAttachments,
    bool HasNotices,
    string Lat,
    string Long,
    double? Latitude,
    double? Longitude);
