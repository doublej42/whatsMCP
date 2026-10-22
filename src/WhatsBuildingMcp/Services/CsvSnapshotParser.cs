using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using WhatsBuildingMcp.Models;

namespace WhatsBuildingMcp.Services;

/// <summary>Tolerant RFC 4180 parser for <c>whatsbuilding.csv</c> (dynamic header, missing columns OK).</summary>
public static class CsvSnapshotParser
{
    public static IReadOnlyList<ApplicationRecord> Parse(TextReader reader, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            HeaderValidated = null,
            BadDataFound = null,
            TrimOptions = TrimOptions.Trim,
            IgnoreBlankLines = true,
        };

        using var csv = new CsvReader(reader, config);
        if (!csv.Read() || !csv.ReadHeader())
        {
            logger?.LogWarning("Upstream CSV has no header row");
            return [];
        }

        var records = new List<ApplicationRecord>();
        while (csv.Read())
        {
            var fileNumber = Field(csv, "FileNumber");
            if (string.IsNullOrWhiteSpace(fileNumber))
            {
                continue;
            }

            records.Add(new ApplicationRecord(
                FileNumber: fileNumber,
                Url: Field(csv, "Url"),
                DateSubmitted: DateUtil.ParseUpstreamDate(Field(csv, "DateSubmitted")),
                CivicAddress: Field(csv, "CivicAddress"),
                Neighbourhood: Field(csv, "Neighbourhood"),
                ApplicationType: Field(csv, "ApplicationType"),
                StaffContact: Field(csv, "StaffContact"),
                Description: TextUtil.Normalize(Field(csv, "Description")),
                Status: Field(csv, "Status"),
                PermitValue: ParseDecimal(Field(csv, "PermitValue")),
                BuildingArea: ParseDouble(Field(csv, "BuildingArea")),
                Applicants: ParseApplicants(Field(csv, "Applicants")),
                IssuedDate: DateUtil.ParseUpstreamDate(Field(csv, "IssuedDate")),
                FinalDate: DateUtil.ParseUpstreamDate(Field(csv, "FinalDate")),
                ExpiryDate: DateUtil.ParseUpstreamDate(Field(csv, "ExpiryDate")),
                Properties: ParseProperties(Field(csv, "Properties")),
                HasAttachments: ParseBool(Field(csv, "HasAttachments")),
                HasNotices: ParseBool(Field(csv, "HasNotices")),
                Lat: Field(csv, "Lat"),
                Long: Field(csv, "Long"),
                Latitude: GeoUtil.TryParseCoordinate(Field(csv, "Lat")),
                Longitude: GeoUtil.TryParseCoordinate(Field(csv, "Long"))));
        }

        return records;
    }

    private static string Field(CsvReader csv, string name)
        => csv.TryGetField<string>(name, out var value) ? value ?? string.Empty : string.Empty;

    private static decimal? ParseDecimal(string text)
        => decimal.TryParse(text, NumberStyles.Currency, DateUtil.EnUs, out var value) ? value : null;

    private static double? ParseDouble(string text)
        => double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var value)
            ? value
            : double.TryParse(text, NumberStyles.Currency, DateUtil.EnUs, out var currency) ? currency : null;

    private static bool ParseBool(string text)
        => bool.TryParse(text, out var value) && value;

    private static IReadOnlyList<Applicant> ParseApplicants(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var result = new List<Applicant>();
        foreach (var entry in text.Split([" , "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = entry.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
            {
                result.Add(new Applicant(null, entry.Trim()));
            }
            else
            {
                result.Add(new Applicant(entry.Substring(0, separator).Trim(), entry.Substring(separator + 1).Trim()));
            }
        }

        return result;
    }

    private static IReadOnlyList<string> ParseProperties(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return text.Split([" , "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
