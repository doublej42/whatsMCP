using WhatsBuildingMcp.Options;

namespace WhatsBuildingMcp.Services;

/// <summary>
/// Defense-in-depth guard: every outbound request must target the configured host,
/// must not embed credentials, and must never touch an <c>Identity/*</c> route.
/// </summary>
public sealed class OutboundHostGuardHandler : DelegatingHandler
{
    private readonly string _allowedHost;

    public OutboundHostGuardHandler(IOptions<WhatsBuildingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _allowedHost = new Uri(options.Value.BaseUrl, UriKind.Absolute).Host;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var uri = request.RequestUri ?? throw new UpstreamException("Blocked outbound request without a URI.");
        if (!string.Equals(uri.Host, _allowedHost, StringComparison.OrdinalIgnoreCase))
        {
            throw new UpstreamException($"Blocked outbound request to non-configured host '{uri.Host}'.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new UpstreamException("Blocked outbound request containing credentials.");
        }

        if (uri.AbsolutePath.Contains("identity", StringComparison.OrdinalIgnoreCase))
        {
            throw new UpstreamException("Blocked outbound request to an Identity route.");
        }

        return base.SendAsync(request, cancellationToken);
    }
}
