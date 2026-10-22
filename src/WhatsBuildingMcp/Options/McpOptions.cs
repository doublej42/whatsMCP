namespace WhatsBuildingMcp.Options;

/// <summary>MCP endpoint security settings. Section <c>Mcp</c>.</summary>
public sealed class McpOptions
{
    public const string SectionName = "Mcp";

    /// <summary>
    /// Optional bearer token protecting the MCP endpoint. When null or empty,
    /// the MCP endpoint is unauthenticated.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Allowed CORS origins for browser-based MCP clients. Empty disables CORS.</summary>
    public string[] AllowedOrigins { get; set; } = [];
}
