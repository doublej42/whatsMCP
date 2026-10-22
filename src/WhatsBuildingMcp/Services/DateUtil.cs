using System.Globalization;

namespace WhatsBuildingMcp.Services;

/// <summary>Upstream date parsing plus Nanaimo-local "today" for activity windows.</summary>
public static class DateUtil
{
    public static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    private static readonly string[] UpstreamFormats =
    [
        "M/d/yyyy h:mm:ss tt",
        "M/d/yyyy hh:mm:ss tt",
        "MM/dd/yyyy h:mm:ss tt",
        "M/d/yyyy",
        "MM/dd/yyyy",
        "yyyy-MM-dd",
        "MMM d, yyyy",
        "MMMM d, yyyy",
    ];

    private static readonly Lazy<TimeZoneInfo> Pacific = new(FindPacific);

    /// <summary>Parses upstream date strings such as "9/18/2012 12:00:00 AM" or "May 24, 2016".</summary>
    public static DateTime? ParseUpstreamDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var value = text.Trim();
        if (DateTime.TryParseExact(value, UpstreamFormats, EnUs, DateTimeStyles.None, out var exact))
        {
            return exact;
        }

        return DateTime.TryParse(value, EnUs, DateTimeStyles.None, out var parsed) ? parsed : null;
    }

    /// <summary>Parses a caller-supplied date parameter (yyyy-MM-dd preferred).</summary>
    public static DateTime? ParseDateParam(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var value = text.Trim();
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", EnUs, DateTimeStyles.None, out var iso))
        {
            return iso.ToDateTime(TimeOnly.MinValue);
        }

        return ParseUpstreamDate(value);
    }

    /// <summary>Current date in Nanaimo (Pacific time), falling back to UTC.</summary>
    public static DateOnly NanaimoToday()
    {
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Pacific.Value);
        return DateOnly.FromDateTime(now.DateTime);
    }

    private static TimeZoneInfo FindPacific()
    {
        foreach (var id in new[] { "America/Vancouver", "Pacific Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Utc;
    }
}
