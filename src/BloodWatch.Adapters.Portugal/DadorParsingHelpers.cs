using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BloodWatch.Adapters.Portugal;

internal static class DadorParsingHelpers
{
    private static readonly CultureInfo PtPtCulture = new("pt-PT");

    public static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        if (element.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    public static string? ReadString(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(element, propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => NormalizeText(property.GetString()),
            JsonValueKind.Number => property.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    public static string? NormalizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static (decimal? Latitude, decimal? Longitude) ParseGeoReference(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return (null, null);
        }

        var fragments = rawValue
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (fragments.Length != 2)
        {
            return (null, null);
        }

        if (!TryParseDecimal(fragments[0], out var latitude) || !TryParseDecimal(fragments[1], out var longitude))
        {
            return (null, null);
        }

        return (latitude, longitude);
    }

    public static bool TryParseDateOnly(string? rawValue, out DateOnly result)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var normalized = rawValue.Trim();

        return DateOnly.TryParseExact(normalized, "dd/MM/yyyy", PtPtCulture, DateTimeStyles.None, out result)
               || DateOnly.TryParseExact(normalized, "dd-MM-yyyy", PtPtCulture, DateTimeStyles.None, out result)
               || DateOnly.TryParse(normalized, PtPtCulture, DateTimeStyles.None, out result);
    }

    public static bool TryParseDateTime(string? rawValue, out DateTime result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var normalized = rawValue.Trim();
        var formats = new[]
        {
            "dd/MM/yyyy HH:mm",
            "dd/MM/yyyy",
            "dd-MM-yyyy HH:mm",
            "dd-MM-yyyy",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd HH:mm",
            "yyyy-MM-dd",
        };

        if (DateTime.TryParseExact(
                normalized,
                formats,
                PtPtCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out result))
        {
            result = DateTime.SpecifyKind(result, DateTimeKind.Utc);
            return true;
        }

        if (DateTime.TryParse(
                normalized,
                PtPtCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out result))
        {
            result = DateTime.SpecifyKind(result, DateTimeKind.Utc);
            return true;
        }

        return false;
    }

    public static string NormalizeMetricDisplayName(string? rawAbo)
    {
        if (string.IsNullOrWhiteSpace(rawAbo))
        {
            return "Unknown";
        }

        var cleaned = rawAbo.Trim().ToUpperInvariant().Replace(" ", string.Empty, StringComparison.Ordinal);
        if (cleaned is "A+" or "A-" or "B+" or "B-" or "AB+" or "AB-" or "O+" or "O-")
        {
            return cleaned;
        }

        return cleaned;
    }

    public static string NormalizeMetricKey(string? rawAbo)
    {
        var display = NormalizeMetricDisplayName(rawAbo);
        if (display == "UNKNOWN")
        {
            return "blood-group-unknown";
        }

        var normalized = display.ToUpperInvariant();
        var suffix = normalized switch
        {
            "A+" => "a-plus",
            "A-" => "a-minus",
            "B+" => "b-plus",
            "B-" => "b-minus",
            "AB+" => "ab-plus",
            "AB-" => "ab-minus",
            "O+" => "o-plus",
            "O-" => "o-minus",
            _ => Slugify(normalized),
        };

        return $"blood-group-{suffix}";
    }

    public static RegionProjection NormalizeRegion(string? rawRegionName)
    {
        var normalized = NormalizeText(rawRegionName);
        if (normalized is null)
        {
            return new RegionProjection("pt-unknown", "Unknown");
        }

        var upper = normalized.ToUpperInvariant();
        return upper switch
        {
            "NORTE" => new RegionProjection("pt-norte", "Norte"),
            "CENTRO" => new RegionProjection("pt-centro", "Centro"),
            "LISBOA E SETUBAL" => new RegionProjection("pt-lisboa-setubal", "Lisboa e Setubal"),
            "ALENTEJO" => new RegionProjection("pt-alentejo", "Alentejo"),
            "ALGARVE" => new RegionProjection("pt-algarve", "Algarve"),
            "NACIONAL" => new RegionProjection("pt-nacional", "Nacional"),
            "IPST" => new RegionProjection("pt-ipst", "IPST"),
            _ => new RegionProjection($"pt-{Slugify(normalized)}", normalized),
        };
    }

    private static bool TryParseDecimal(string rawValue, out decimal result)
    {
        return decimal.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
               || decimal.TryParse(rawValue, NumberStyles.Float, PtPtCulture, out result);
    }

    private static string Slugify(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var previousWasHyphen = false;

        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var lower = char.ToLowerInvariant(character);
            if (char.IsLetterOrDigit(lower))
            {
                builder.Append(lower);
                previousWasHyphen = false;
                continue;
            }

            if (previousWasHyphen)
            {
                continue;
            }

            builder.Append('-');
            previousWasHyphen = true;
        }

        var slug = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "unknown" : slug;
    }

    public readonly record struct RegionProjection(string Key, string DisplayName);
}
