using System.Text.RegularExpressions;

namespace WhatsBuildingMcp.Services;

/// <summary>Small text helpers for normalizing untrusted upstream text.</summary>
public static partial class TextUtil
{
    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankRuns();

    /// <summary>Unifies newlines, collapses 3+ blank lines and trims. Never returns null.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var unified = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = unified.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].Trim();
        }

        return BlankRuns().Replace(string.Join('\n', lines), "\n\n").Trim();
    }

    public static string? NullIfEmpty(string? text)
    {
        var normalized = Normalize(text);
        return normalized.Length == 0 ? null : normalized;
    }

    public static string Truncate(string text, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (maxLength < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLength));
        }

        var trimmed = text.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed.Substring(0, maxLength).TrimEnd() + "…";
    }
}
