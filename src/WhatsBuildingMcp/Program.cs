using System.Net.Mime;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using Polly;
using WhatsBuildingMcp.Middleware;
using WhatsBuildingMcp.Options;
using WhatsBuildingMcp.Services;
using WhatsBuildingMcp.Tools;

var builder = WebApplication.CreateBuilder(args);

// Structured console logging (JSON). The API key is never logged.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
});

// Options (env vars use __ separators, e.g. WhatsBuilding__BaseUrl).
builder.Services.AddOptions<WhatsBuildingOptions>().Bind(builder.Configuration.GetSection(WhatsBuildingOptions.SectionName));
builder.Services.AddOptions<McpOptions>().Bind(builder.Configuration.GetSection(McpOptions.SectionName));
builder.Services.AddOptions<RateLimitOptions>().Bind(builder.Configuration.GetSection(RateLimitOptions.SectionName));

var whatsBuilding = new WhatsBuildingOptions();
builder.Configuration.GetSection(WhatsBuildingOptions.SectionName).Bind(whatsBuilding);
whatsBuilding.Validate();

var mcpSecurity = new McpOptions();
builder.Configuration.GetSection(McpOptions.SectionName).Bind(mcpSecurity);

var rateLimit = new RateLimitOptions();
builder.Configuration.GetSection(RateLimitOptions.SectionName).Bind(rateLimit);
rateLimit.Validate();

// Reverse-proxy support (X-Forwarded-For / X-Forwarded-Proto).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddMemoryCache();

// Typed upstream client: good-citizen User-Agent, same-host guard, retry + timeout.
builder.Services.AddTransient<OutboundHostGuardHandler>();
builder.Services.AddHttpClient<WhatsBuildingClient>(client =>
    {
        client.BaseAddress = new Uri(whatsBuilding.BaseUrl, UriKind.Absolute);
        client.Timeout = Timeout.InfiniteTimeSpan; // Polly owns timeouts below.
        client.DefaultRequestHeaders.UserAgent.ParseAdd(whatsBuilding.UserAgent);
    })
    .AddHttpMessageHandler<OutboundHostGuardHandler>()
    .AddResilienceHandler("whatsbuilding", pipeline =>
    {
        pipeline.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            Delay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(8),
        });
        pipeline.AddTimeout(TimeSpan.FromSeconds(whatsBuilding.HttpTimeoutSeconds));
    });

builder.Services.AddSingleton<ISnapshotService, SnapshotService>();
builder.Services.AddHostedService(static provider => (SnapshotService)provider.GetRequiredService<ISnapshotService>());
builder.Services.AddSingleton<IReferenceDataService, ReferenceDataService>();
builder.Services.AddSingleton<IDetailService, DetailService>();
builder.Services.AddSingleton<IUpstreamContentService, UpstreamContentService>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("mcp", limiter =>
    {
        limiter.PermitLimit = rateLimit.PermitLimit;
        limiter.Window = TimeSpan.FromSeconds(rateLimit.WindowSeconds);
        limiter.QueueLimit = rateLimit.QueueLimit;
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});

var allowedOrigins = mcpSecurity.AllowedOrigins
    .Where(o => !string.IsNullOrWhiteSpace(o))
    .ToArray();
if (allowedOrigins.Length > 0)
{
    builder.Services.AddCors(options => options.AddPolicy("mcp", policy => policy
        .WithOrigins(allowedOrigins)
        .WithMethods("GET", "POST", "DELETE")
        .WithHeaders("Content-Type", "MCP-Protocol-Version", "Mcp-Session-Id")
        .WithExposedHeaders("Mcp-Session-Id")));
}

// MCP server over Streamable HTTP (stateless: no session affinity needed).
builder.Services.AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation
        {
            Name = "whatsbuilding-mcp",
            Version = "1.0.0",
            Title = "What's Building (City of Nanaimo)",
            Description = "Read-only Model Context Protocol server for the City of Nanaimo What's Building public data: "
                + "development applications, building permits and subdivisions. Public data only; may lag city systems; not legal advice.",
        };
    })
    .WithHttpTransport()
    .WithTools<WhatsBuildingTools>()
    .WithResources<WhatsBuildingResources>()
    .WithPrompts<WhatsBuildingPrompts>();

var app = builder.Build();

app.UseForwardedHeaders();
if (allowedOrigins.Length > 0)
{
    app.UseCors();
}

app.UseRateLimiter();
app.UseMiddleware<ApiKeyMiddleware>();

app.MapGet("/health", async (ISnapshotService snapshots, CancellationToken cancellationToken) =>
{
    var snapshot = await snapshots.GetSnapshotAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
    var age = (long)Math.Max(0, (DateTimeOffset.UtcNow - snapshot.FetchedUtc).TotalSeconds);
    return Results.Json(
        new
        {
            status = snapshot.Records.Count == 0 ? "degraded" : "healthy",
            recordCount = snapshot.Records.Count,
            snapshotAgeSeconds = age,
            lastRefreshUtc = snapshot.FetchedUtc,
            stale = snapshot.IsStale,
            warning = snapshot.Warning,
        },
        contentType: MediaTypeNames.Application.Json);
});

var mcp = app.MapMcp("/mcp").RequireRateLimiting("mcp");
if (allowedOrigins.Length > 0)
{
    mcp.RequireCors("mcp");
}

app.Run();
