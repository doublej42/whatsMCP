namespace WhatsBuildingMcp.Options;

/// <summary>Inbound rate limiting for the MCP endpoint. Section <c>RateLimit</c>.</summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimit";

    public int PermitLimit { get; set; } = 60;

    public int WindowSeconds { get; set; } = 60;

    public int QueueLimit { get; set; } = 10;

    /// <exception cref="InvalidOperationException">Thrown when any setting is invalid.</exception>
    public void Validate()
    {
        if (PermitLimit <= 0 || WindowSeconds <= 0 || QueueLimit < 0)
        {
            throw new InvalidOperationException("RateLimit:PermitLimit and WindowSeconds must be positive; QueueLimit must be non-negative.");
        }
    }
}
