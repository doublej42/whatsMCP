namespace WhatsBuildingMcp.Options;

/// <summary>Upstream What's Building site settings. Section <c>WhatsBuilding</c>.</summary>
public sealed class WhatsBuildingOptions
{
    public const string SectionName = "WhatsBuilding";

    /// <summary>Base URL of the public What's Building app. Must end with a slash.</summary>
    public string BaseUrl { get; set; } = "https://www.nanaimo.ca/whatsbuilding/";

    /// <summary>How often the CSV snapshot is refreshed.</summary>
    public int CacheMinutes { get; set; } = 30;

    /// <summary>Total timeout for a single upstream request, in seconds.</summary>
    public int HttpTimeoutSeconds { get; set; } = 60;

    /// <summary>Descriptive User-Agent sent to the upstream site.</summary>
    public string UserAgent { get; set; } = "WhatsBuildingMcp/1.0 (+https://www.nanaimo.ca/whatsbuilding/)";

    /// <summary>Maximum accepted size of the CSV snapshot download.</summary>
    public long MaxCsvBytes { get; set; } = 25L * 1024 * 1024;

    /// <summary>Maximum accepted size of any other upstream response.</summary>
    public long MaxResponseBytes { get; set; } = 10L * 1024 * 1024;

    /// <summary>TTL for parsed per-application detail pages.</summary>
    public int DetailCacheMinutes { get; set; } = 15;

    /// <summary>TTL for the scraped search-form reference data.</summary>
    public int ReferenceDataCacheHours { get; set; } = 24;

    /// <summary>TTL for upstream date-window marker queries.</summary>
    public int RecentActivityCacheMinutes { get; set; } = 15;

    /// <summary>Fail-fast validation of the bound options.</summary>
    /// <exception cref="InvalidOperationException">Thrown when any setting is invalid.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl)
            || !Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException("WhatsBuilding:BaseUrl must be an absolute http(s) URL.");
        }

        if (!string.IsNullOrEmpty(baseUri.UserInfo))
        {
            throw new InvalidOperationException("WhatsBuilding:BaseUrl must not contain credentials.");
        }

        if (CacheMinutes is < 1 or > 1440)
        {
            throw new InvalidOperationException("WhatsBuilding:CacheMinutes must be between 1 and 1440.");
        }

        if (HttpTimeoutSeconds is < 5 or > 300)
        {
            throw new InvalidOperationException("WhatsBuilding:HttpTimeoutSeconds must be between 5 and 300.");
        }

        if (string.IsNullOrWhiteSpace(UserAgent))
        {
            throw new InvalidOperationException("WhatsBuilding:UserAgent must not be empty.");
        }

        if (MaxCsvBytes <= 0 || MaxResponseBytes <= 0)
        {
            throw new InvalidOperationException("WhatsBuilding:MaxCsvBytes and MaxResponseBytes must be positive.");
        }

        if (DetailCacheMinutes is < 1 or > 1440)
        {
            throw new InvalidOperationException("WhatsBuilding:DetailCacheMinutes must be between 1 and 1440.");
        }

        if (ReferenceDataCacheHours is < 1 or > 168)
        {
            throw new InvalidOperationException("WhatsBuilding:ReferenceDataCacheHours must be between 1 and 168.");
        }

        if (RecentActivityCacheMinutes is < 1 or > 1440)
        {
            throw new InvalidOperationException("WhatsBuilding:RecentActivityCacheMinutes must be between 1 and 1440.");
        }
    }
}
