using System.ComponentModel;
using ModelContextProtocol.Server;
using WhatsBuildingMcp.Services;

namespace WhatsBuildingMcp.Tools;

/// <summary>MCP resources: about text and per-application JSON detail.</summary>
[McpServerResourceType]
public sealed class WhatsBuildingResources(IUpstreamContentService content, IDetailService details)
{
    private readonly IUpstreamContentService _content = content;
    private readonly IDetailService _details = details;

    [McpServerResource(UriTemplate = "whatsbuilding://about", Name = "About What's Building", Title = "About What's Building", MimeType = "text/plain")]
    [Description("Help and about text for the City of Nanaimo What's Building public service.")]
    public async Task<string> GetAboutAsync(CancellationToken cancellationToken = default)
    {
        var result = await _content.GetAboutTextAsync(cancellationToken).ConfigureAwait(false);
        return result.Warning is null ? result.Text : result.Text + "\n\nNote: " + result.Warning;
    }

    [McpServerResource(UriTemplate = "whatsbuilding://application/{fileNumber}", Name = "Application detail", Title = "Application detail", MimeType = "application/json")]
    [Description("Full detail for one What's Building application as JSON. City of Nanaimo public data; may lag the city's systems; not legal advice.")]
    public async Task<string> GetApplicationAsync(
        [Description("File number, e.g. 'DP001002'.")] string fileNumber,
        CancellationToken cancellationToken = default)
    {
        if (!InputValidation.IsValidFileNumber(fileNumber))
        {
            return McpJson.Serialize(new { fileNumber, found = false, error = "Invalid file number. Use 3-30 letters, digits, '-' or '_'." });
        }

        var detail = await _details.GetApplicationAsync(fileNumber, cancellationToken).ConfigureAwait(false);
        if (detail is null)
        {
            return McpJson.Serialize(new { fileNumber, found = false, error = $"Application '{fileNumber}' was not found or is not public." });
        }

        return McpJson.Serialize(detail);
    }
}
