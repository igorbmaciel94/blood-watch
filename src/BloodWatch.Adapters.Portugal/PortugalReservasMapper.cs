using System.Globalization;
using System.Text;
using System.Text.Json;
using BloodWatch.Core.Models;

namespace BloodWatch.Adapters.Portugal;

public sealed class PortugalReservasMapper
{
    private static readonly SourceRef Source = new(PortugalAdapter.DefaultAdapterKey, "Portugal SNS Transparency");
    private static readonly CultureInfo PtPtCulture = new("pt-PT");
    private const string Unit = "units";
    private const string UnknownRegionDisplayName = "Unknown region";

    public Snapshot Map(JsonElement payloadRoot, DateTime capturedAtUtc)
    {
        var parsedRows = new List<ParsedRow>();
        DateOnly? latestReferenceDate = null;

        foreach (var record in EnumerateRecords(payloadRoot))
        {
            var fields = ResolveFieldsObject(record);
            if (!TryGetReferenceDate(fields, out var referenceDate))
            {
                continue;
            }

            latestReferenceDate = latestReferenceDate is null || referenceDate > latestReferenceDate
                ? referenceDate
                : latestReferenceDate;

            if (!TryGetDecimal(fields, ["reservas", "reserva", "value", "valor"], out var value))
            {
                continue;
            }

            var regionDisplayName = GetString(fields, ["regiao", "regiao_de_saude", "regiao_saude"])
                ?? GetString(fields, ["entidade", "hospital"])
                ?? UnknownRegionDisplayName;

            var metricRaw = GetString(fields, ["grupo_sanguineo", "grupo", "blood_group"]) ?? string.Empty;

            parsedRows.Add(new ParsedRow(referenceDate, regionDisplayName, metricRaw, value));
        }

        var capturedAtUtcFixed = EnsureUtc(capturedAtUtc);
        if (latestReferenceDate is null)
        {
            return new Snapshot(
                Source,
                capturedAtUtcFixed,
                DateOnly.FromDateTime(capturedAtUtcFixed),
                Items: []);
        }

        var latestRows = parsedRows
            .Where(row => row.ReferenceDate == latestReferenceDate.Value)
            .ToArray();

        var hasBreakdown = latestRows.Any(row => !IsOverallMetric(row.MetricRaw));

        var groupedItems = latestRows
            .Select(row => MapRowToSnapshotItem(row, hasBreakdown))
            .GroupBy(item => (item.Region.Key, item.Metric.Key, item.Unit))
            .Select(group =>
            {
                var first = group.First();
                var total = group.Sum(item => item.Value);
                return new SnapshotItem(first.Metric, first.Region, total, first.Unit, first.Severity);
            })
            .OrderBy(item => item.Region.Key, StringComparer.Ordinal)
            .ThenBy(item => item.Metric.Key, StringComparer.Ordinal)
            .ToArray();

        return new Snapshot(
            Source,
            capturedAtUtcFixed,
            latestReferenceDate,
            groupedItems);
    }

    private static IEnumerable<JsonElement> EnumerateRecords(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var record in root.EnumerateArray())
            {
                yield return record;
            }

            yield break;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        if (TryGetPropertyIgnoreCase(root, "results", out var results)
            && results.ValueKind == JsonValueKind.Array)
        {
            foreach (var record in results.EnumerateArray())
            {
                yield return record;
            }

            yield break;
        }

        if (TryGetPropertyIgnoreCase(root, "records", out var records)
            && records.ValueKind == JsonValueKind.Array)
        {
            foreach (var record in records.EnumerateArray())
            {
                yield return record;
            }
        }
    }

    private static JsonElement ResolveFieldsObject(JsonElement record)
    {
        if (record.ValueKind == JsonValueKind.Object
            && TryGetPropertyIgnoreCase(record, "fields", out var fields)
            && fields.ValueKind == JsonValueKind.Object)
        {
            return fields;
        }

        return record;
    }

    private static SnapshotItem MapRowToSnapshotItem(ParsedRow row, bool hasBreakdown)
    {
        var regionDisplay = string.IsNullOrWhiteSpace(row.RegionDisplayName)
            ? UnknownRegionDisplayName
            : row.RegionDisplayName.Trim();

        var region = new RegionRef(
            NormalizeRegionKey(regionDisplay),
            regionDisplay);

        var metric = NormalizeMetric(row.MetricRaw, hasBreakdown);

        return new SnapshotItem(metric, region, row.Value, Unit);
    }

    private static Metric NormalizeMetric(string rawMetric, bool hasBreakdown)
    {
        if (!hasBreakdown || IsOverallMetric(rawMetric))
        {
            return new Metric("overall", "Overall", Unit);
        }

        var displayName = rawMetric.Trim();
        var key = Slugify(
            displayName
                .Replace("+", " plus ", StringComparison.Ordinal)
                .Replace("-", " minus ", StringComparison.Ordinal));

        if (string.IsNullOrWhiteSpace(key))
        {
            return new Metric("overall", "Overall", Unit);
        }

        return new Metric($"blood-group-{key}", displayName, Unit);
    }

    private static string NormalizeRegionKey(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "pt-unknown";
        }

        var normalized = Slugify(displayName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "pt-unknown";
        }

        normalized = normalized
            .Replace("regiao-de-saude-do-", string.Empty, StringComparison.Ordinal)
            .Replace("regiao-de-saude-", string.Empty, StringComparison.Ordinal)
            .Trim('-');

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "pt-unknown";
        }

        if (normalized.Contains("norte", StringComparison.Ordinal))
        {
            return "pt-norte";
        }

        if (normalized.Contains("centro", StringComparison.Ordinal))
        {
            return "pt-centro";
        }

        if (normalized.Contains("lvt", StringComparison.Ordinal)
            || normalized.Contains("lisboa-vale-tejo", StringComparison.Ordinal))
        {
            return "pt-lvt";
        }

        if (normalized.Contains("alentejo", StringComparison.Ordinal))
        {
            return "pt-alentejo";
        }

        if (normalized.Contains("algarve", StringComparison.Ordinal))
        {
            return "pt-algarve";
        }

        return $"pt-{normalized}";
    }

    private static bool IsOverallMetric(string rawMetric)
    {
        if (string.IsNullOrWhiteSpace(rawMetric))
        {
            return true;
        }

        var normalized = Slugify(rawMetric);
        return normalized is "overall" or "total" or "all" or "todos" or "todas";
    }

    private static bool TryGetReferenceDate(JsonElement fields, out DateOnly referenceDate)
    {
        referenceDate = default;

        var rawPeriod = GetString(fields, ["periodo", "periodo_referencia", "period"]);
        if (string.IsNullOrWhiteSpace(rawPeriod))
        {
            return false;
        }

        var cleaned = rawPeriod.Trim();
        if (TryParseYearMonth(cleaned, out referenceDate))
        {
            return true;
        }

        if (DateTime.TryParse(cleaned, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedDateTime))
        {
            referenceDate = new DateOnly(parsedDateTime.Year, parsedDateTime.Month, 1);
            return true;
        }

        return false;
    }

    private static bool TryParseYearMonth(string rawValue, out DateOnly referenceDate)
    {
        referenceDate = default;
        var fragments = rawValue
            .Split(['-', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (fragments.Length >= 2
            && TryParseYearAndMonth(fragments[0], fragments[1], out referenceDate))
        {
            return true;
        }

        if (rawValue.Length == 6
            && int.TryParse(rawValue.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            && int.TryParse(rawValue.AsSpan(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var month)
            && month is >= 1 and <= 12)
        {
            referenceDate = new DateOnly(year, month, 1);
            return true;
        }

        return false;
    }

    private static bool TryParseYearAndMonth(string first, string second, out DateOnly referenceDate)
    {
        referenceDate = default;

        if (!int.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out var firstValue)
            || !int.TryParse(second, NumberStyles.None, CultureInfo.InvariantCulture, out var secondValue))
        {
            return false;
        }

        if (first.Length == 4 && secondValue is >= 1 and <= 12)
        {
            referenceDate = new DateOnly(firstValue, secondValue, 1);
            return true;
        }

        if (second.Length == 4 && firstValue is >= 1 and <= 12)
        {
            referenceDate = new DateOnly(secondValue, firstValue, 1);
            return true;
        }

        return false;
    }

    private static bool TryGetDecimal(JsonElement fields, string[] propertyNames, out decimal value)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetPropertyIgnoreCase(fields, propertyName, out var rawValue))
            {
                continue;
            }

            if (TryParseDecimal(rawValue, out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryParseDecimal(JsonElement rawValue, out decimal value)
    {
        switch (rawValue.ValueKind)
        {
            case JsonValueKind.Number:
                if (rawValue.TryGetDecimal(out value))
                {
                    return true;
                }

                if (rawValue.TryGetDouble(out var doubleValue))
                {
                    value = Convert.ToDecimal(doubleValue, CultureInfo.InvariantCulture);
                    return true;
                }

                break;

            case JsonValueKind.String:
                return TryParseDecimalString(rawValue.GetString(), out value);
        }

        value = default;
        return false;
    }

    private static bool TryParseDecimalString(string? rawValue, out decimal value)
    {
        value = default;

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var cleaned = rawValue.Trim();
        if (decimal.TryParse(cleaned, NumberStyles.Number, PtPtCulture, out value))
        {
            return true;
        }

        if (decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        cleaned = cleaned.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (cleaned.Contains(',', StringComparison.Ordinal) && cleaned.Contains('.', StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace(".", string.Empty, StringComparison.Ordinal);
        }

        cleaned = cleaned.Replace(',', '.');
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static string? GetString(JsonElement fields, string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetPropertyIgnoreCase(fields, propertyName, out var rawValue))
            {
                continue;
            }

            if (rawValue.ValueKind == JsonValueKind.String)
            {
                var result = rawValue.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(result))
                {
                    return result;
                }
            }
            else if (rawValue.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
            {
                return rawValue.ToString();
            }
        }

        return null;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
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

    private static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var previousWasHyphen = false;

        foreach (var character in normalized)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(character);
            if (unicodeCategory == UnicodeCategory.NonSpacingMark)
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

        return builder.ToString().Trim('-');
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }

    private sealed record ParsedRow(DateOnly ReferenceDate, string RegionDisplayName, string MetricRaw, decimal Value);
}
