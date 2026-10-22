using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using WhatsBuildingMcp.Middleware;
using WhatsBuildingMcp.Options;
using MOptions = Microsoft.Extensions.Options.Options;

namespace WhatsBuildingMcp.Tests;

public sealed class ApiKeyMiddlewareTests
{
    [Fact]
    public async Task Open_By_Default()
    {
        var (context, next) = Arrange("/mcp", authorization: null);

        await new ApiKeyMiddleware(next.Invoke, MOptions.Create(new McpOptions())).InvokeAsync(context);

        Assert.True(next.Called);
    }

    [Fact]
    public async Task Health_Stays_Open_When_Key_Configured()
    {
        var (context, next) = Arrange("/health", authorization: null);

        await new ApiKeyMiddleware(next.Invoke, MOptions.Create(new McpOptions { ApiKey = "secret" })).InvokeAsync(context);

        Assert.True(next.Called);
    }

    [Fact]
    public async Task Mcp_Requires_Bearer_Key()
    {
        var (context, next) = Arrange("/mcp", authorization: "Bearer secret");

        await new ApiKeyMiddleware(next.Invoke, MOptions.Create(new McpOptions { ApiKey = "secret" })).InvokeAsync(context);

        Assert.True(next.Called);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Bearer wrong")]
    [InlineData("Basic c2VjcmV0")]
    [InlineData("Bearer ")]
    public async Task Mcp_Rejects_Missing_Or_Wrong_Key(string? authorization)
    {
        var (context, next) = Arrange("/mcp", authorization);

        await new ApiKeyMiddleware(next.Invoke, MOptions.Create(new McpOptions { ApiKey = "secret" })).InvokeAsync(context);

        Assert.False(next.Called);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    private static (DefaultHttpContext Context, NextProbe Next) Arrange(string path, string? authorization)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (authorization is not null)
        {
            context.Request.Headers.Authorization = authorization;
        }

        return (context, new NextProbe());
    }

    private sealed class NextProbe
    {
        public bool Called { get; private set; }

        public Task Invoke(HttpContext context)
        {
            Called = true;
            return Task.CompletedTask;
        }
    }
}
