using System.Text.Json;
using BloodWatch.Core.Models;

namespace BloodWatch.Adapters.Portugal;

public sealed class PortugalReservasMapper
{
    private static readonly SourceRef Source = new(PortugalAdapter.DefaultAdapterKey, PortugalAdapter.DefaultSourceName);

    public Snapshot Map(JsonElement payloadRoot, DateTime capturedAtUtc)
    {
        var capturedAtUtcFixed = EnsureUtc(capturedAtUtc);

        if (!TryResolveReservasRoot(payloadRoot, out var reservasRoot, out var sourceVersionRaw))
        {
            return new Snapshot(
                Source,
                capturedAtUtcFixed,
                DateOnly.FromDateTime(capturedAtUtcFixed),
                Items: []);
        }

        DateOnly? referenceDate = TryResolveReferenceDate(reservasRoot, sourceVersionRaw);
        DateTime? sourceUpdatedAtUtc = TryResolveSourceUpdatedAtUtc(sourceVersionRaw);

        var mappedItems = new List<SnapshotItem>();

        if (DadorParsingHelpers.TryGetPropertyIgnoreCase(reservasRoot, "IPST", out var ipstRows)
            && ipstRows.ValueKind == JsonValueKind.Array)
        {
            ParseFlatRegionRows(ipstRows, DadorParsingHelpers.NormalizeRegion("IPST"), mappedItems);
        }

        if (DadorParsingHelpers.TryGetPropertyIgnoreCase(reservasRoot, "NACIONAL", out var nationalRows)
            && nationalRows.ValueKind == JsonValueKind.Array)
        {
            ParseFlatRegionRows(nationalRows, DadorParsingHelpers.NormalizeRegion("NACIONAL"), mappedItems);
        }

        if (DadorParsingHelpers.TryGetPropertyIgnoreCase(reservasRoot, "Regiao", out var regionalRows)
            && regionalRows.ValueKind == JsonValueKind.Array)
        {
            ParseRegionalRows(regionalRows, mappedItems);
        }

        var deduplicatedItems = mappedItems
            .GroupBy(item => (item.Region.Key, item.Metric.Key))
            .Select(group => group
                .OrderByDescending(item => ReserveStatusCatalog.GetRank(item.StatusKey))
                .First())
            .OrderBy(item => item.Region.Key, StringComparer.Ordinal)
            .ThenBy(item => item.Metric.Key, StringComparer.Ordinal)
            .ToArray();

        return new Snapshot(
            Source,
            capturedAtUtcFixed,
            referenceDate,
            deduplicatedItems,
            sourceUpdatedAtUtc);
    }

    private static bool TryResolveReservasRoot(
        JsonElement payloadRoot,
        out JsonElement reservasRoot,
        out string? sourceVersionRaw)
    {
        reservasRoot = default;
        sourceVersionRaw = null;

        if (!DadorParsingHelpers.TryGetPropertyIgnoreCase(payloadRoot, "data", out var dataNode)
            || dataNode.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        sourceVersionRaw = DadorParsingHelpers.ReadString(dataNode, "version");

        if (!DadorParsingHelpers.TryGetPropertyIgnoreCase(dataNode, "ReservasSangue", out reservasRoot)
            || reservasRoot.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return true;
    }

    private static DateOnly? TryResolveReferenceDate(JsonElement reservasRoot, string? sourceVersionRaw)
    {
        if (DadorParsingHelpers.TryGetPropertyIgnoreCase(reservasRoot, "Data", out var dataBlock)
            && dataBlock.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in dataBlock.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var cell in row.EnumerateArray())
                {
                    if (cell.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    if (DadorParsingHelpers.TryParseDateOnly(cell.GetString(), out var parsedDate))
                    {
                        return parsedDate;
                    }
                }
            }
        }

        if (DadorParsingHelpers.TryParseDateTime(sourceVersionRaw, out var sourceUpdatedAtUtc))
        {
            return DateOnly.FromDateTime(sourceUpdatedAtUtc);
        }

        return null;
    }

    private static DateTime? TryResolveSourceUpdatedAtUtc(string? sourceVersionRaw)
    {
        return DadorParsingHelpers.TryParseDateTime(sourceVersionRaw, out var parsed)
            ? parsed
            : null;
    }

    private static void ParseRegionalRows(JsonElement rows, IList<SnapshotItem> mappedItems)
    {
        var currentRegion = DadorParsingHelpers.NormalizeRegion("unknown");

        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind == JsonValueKind.Array)
            {
                currentRegion = ResolveRegionHeader(row, currentRegion);
                continue;
            }

            if (row.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (TryMapStatusItem(row, currentRegion, out var mappedItem))
            {
                mappedItems.Add(mappedItem);
            }
        }
    }

    private static DadorParsingHelpers.RegionProjection ResolveRegionHeader(
        JsonElement row,
        DadorParsingHelpers.RegionProjection fallback)
    {
        foreach (var cell in row.EnumerateArray())
        {
            if (cell.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var value = DadorParsingHelpers.NormalizeText(cell.GetString());
            if (value is null)
            {
                continue;
            }

            return DadorParsingHelpers.NormalizeRegion(value);
        }

        return fallback;
    }

    private static void ParseFlatRegionRows(
        JsonElement rows,
        DadorParsingHelpers.RegionProjection region,
        IList<SnapshotItem> mappedItems)
    {
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (TryMapStatusItem(row, region, out var mappedItem))
            {
                mappedItems.Add(mappedItem);
            }
        }
    }

    private static bool TryMapStatusItem(
        JsonElement row,
        DadorParsingHelpers.RegionProjection region,
        out SnapshotItem mappedItem)
    {
        var rawMetric = DadorParsingHelpers.ReadString(row, "ABO");
        if (rawMetric is null)
        {
            mappedItem = default!;
            return false;
        }

        var metric = new Metric(
            DadorParsingHelpers.NormalizeMetricKey(rawMetric),
            DadorParsingHelpers.NormalizeMetricDisplayName(rawMetric));

        var rawColor = DadorParsingHelpers.ReadString(row, "Cor");
        var statusKey = MapColorToStatusKey(rawColor);

        mappedItem = new SnapshotItem(
            metric,
            new RegionRef(region.Key, region.DisplayName),
            statusKey,
            ReserveStatusCatalog.GetLabel(statusKey),
            Value: null,
            Unit: null);

        return true;
    }

    private static string MapColorToStatusKey(string? rawColor)
    {
        var normalized = rawColor?.Trim().ToUpperInvariant();
        return normalized switch
        {
            "VERMELHO" => ReserveStatusCatalog.Critical,
            "LARANJA" => ReserveStatusCatalog.Warning,
            "AMARELO" => ReserveStatusCatalog.Watch,
            "VERDE" => ReserveStatusCatalog.Normal,
            _ => ReserveStatusCatalog.Unknown,
        };
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
}
