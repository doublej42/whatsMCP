namespace WhatsBuildingMcp.Models;

/// <summary>Parsed <c>/FolderView/{fileNumber}</c> detail sections.</summary>
public sealed record DetailProperty(string Address, string? Zone, string? ZoneUrl, string? MoreText, string? MoreUrl);

/// <summary>Parsed attachment entry.</summary>
public sealed record DetailAttachment(string Name, string Url, DateTime? PostedDate);

/// <summary>Parsed hyperlink.</summary>
public sealed record DetailLink(string Text, string Url);

/// <summary>Parsed "Important Dates &amp; Notices" entry.</summary>
public sealed record DetailNotice(
    string Title,
    string? Location,
    string? Date,
    string? Time,
    string? Details,
    IReadOnlyList<DetailLink> Links,
    DateTime? PostedDate);

/// <summary>Parsed process-step row.</summary>
public sealed record ProcessStep(string Task, string? Detail, string? Status);

/// <summary>Parsed policy/regulation link.</summary>
public sealed record RelatedPolicy(string Title, string Url);

/// <summary>Full parsed result of a <c>/FolderView/{fileNumber}</c> page.</summary>
public sealed record FolderViewDetail(
    bool Found,
    string FileNumber,
    string? CivicAddress,
    string? Neighbourhood,
    string? ApplicationType,
    DateTime? DateSubmitted,
    string? StaffContact,
    string? Description,
    string? Status,
    DateTime? IssuedDate,
    DateTime? CloseDate,
    DateTime? ExpiryDate,
    IReadOnlyList<DetailProperty> Properties,
    IReadOnlyList<DetailAttachment> Attachments,
    IReadOnlyList<DetailNotice> Notices,
    string? EducationalNote,
    IReadOnlyList<ProcessStep> ProcessSteps,
    IReadOnlyList<RelatedPolicy> Policies,
    string? PublicUrl,
    double? Latitude,
    double? Longitude)
{
    public static FolderViewDetail NotFound(string fileNumber) => new(
        Found: false,
        FileNumber: fileNumber,
        CivicAddress: null,
        Neighbourhood: null,
        ApplicationType: null,
        DateSubmitted: null,
        StaffContact: null,
        Description: null,
        Status: null,
        IssuedDate: null,
        CloseDate: null,
        ExpiryDate: null,
        Properties: [],
        Attachments: [],
        Notices: [],
        EducationalNote: null,
        ProcessSteps: [],
        Policies: [],
        PublicUrl: null,
        Latitude: null,
        Longitude: null);
}
