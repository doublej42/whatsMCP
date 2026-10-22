using System.Text;
using System.Text.RegularExpressions;

namespace WhatsBuildingMcp.Services;

/// <summary>Caller-input validation and normalization (SSRF prevention).</summary>
public static partial class InputValidation
{
    [GeneratedRegex("^[A-Za-z0-9\\-_]{3,30}$")]
    private static partial Regex FileNumberRegex();

    /// <summary>File numbers must match ^[A-Za-z0-9\-_]{3,30}$ before use in any upstream URL.</summary>
    public static bool IsValidFileNumber(string? fileNumber)
    {
        return !string.IsNullOrWhiteSpace(fileNumber) && FileNumberRegex().IsMatch(fileNumber);
    }

    /// <summary>Trims, strips control characters and clamps length. Never returns null.</summary>
    public static string CleanSearchText(string? text, int maxLength = 200)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var ch in text.Trim())
        {
            if (!char.IsControl(ch))
            {
                builder.Append(ch);
            }
        }

        var cleaned = builder.ToString().Trim();
        return cleaned.Length <= maxLength ? cleaned : cleaned.Substring(0, maxLength);
    }
}
