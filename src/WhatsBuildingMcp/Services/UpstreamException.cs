namespace WhatsBuildingMcp.Services;

/// <summary>Failure talking to the upstream What's Building site (or a blocked request).</summary>
public sealed class UpstreamException : Exception
{
    public UpstreamException(string message)
        : base(message)
    {
    }

    public UpstreamException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
