using System.Security.Cryptography;
using System.Text;
using WhatsBuildingMcp.Options;

namespace WhatsBuildingMcp.Middleware;

/// <summary>
/// Requires <c>Authorization: Bearer &lt;Mcp__ApiKey&gt;</c> on the MCP endpoint
/// when an API key is configured. The health endpoint stays open.
/// </summary>
public sealed class ApiKeyMiddleware(RequestDelegate next, IOptions<McpOptions> options)
{
    private readonly RequestDelegate _next = next;
    private readonly IOptions<McpOptions> _options = options;

    public async Task InvokeAsync(HttpContext context)
    {
        var configured = _options.Value.ApiKey;
        if (!string.IsNullOrEmpty(configured)
            && context.Request.Path.StartsWithSegments("/mcp", StringComparison.OrdinalIgnoreCase)
            && !IsValidBearer(context.Request.Headers.Authorization.ToString(), configured))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" }, context.RequestAborted).ConfigureAwait(false);
            return;
        }

        await _next(context).ConfigureAwait(false);
    }

    internal static bool IsValidBearer(string? header, string configured)
    {
        const string prefix = "Bearer ";
        if (string.IsNullOrEmpty(header) || !header.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var provided = Encoding.UTF8.GetBytes(header.Substring(prefix.Length).Trim());
        var expected = Encoding.UTF8.GetBytes(configured);
        return provided.Length == expected.Length
            && CryptographicOperations.FixedTimeEquals(provided, expected);
    }
}
