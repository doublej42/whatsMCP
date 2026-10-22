namespace WhatsBuildingMcp.Services;

/// <summary>Compact JSON serialization shared by all tool and resource outputs.</summary>
public static class McpJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
