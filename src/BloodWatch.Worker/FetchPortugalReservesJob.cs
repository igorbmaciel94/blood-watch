using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using BloodWatch.Adapters.Portugal;
using BloodWatch.Core.Contracts;
using BloodWatch.Core.Models;
using BloodWatch.Infrastructure.Persistence;
using BloodWatch.Infrastructure.Persistence.Entities;
using BloodWatch.Worker.Dispatch;
using Microsoft.EntityFrameworkCore;

namespace BloodWatch.Worker;

public sealed class FetchPortugalReservesJob(
    IEnumerable<IDataSourceAdapter> adapters,
    IEnumerable<IRule> rules,
    BloodWatchDbContext dbContext,
    DispatchEngine dispatchEngine,
    ILogger<FetchPortugalReservesJob> logger)
{
    private readonly IReadOnlyCollection<IDataSourceAdapter> _adapters = adapters.ToArray();
    private readonly IReadOnlyCollection<IRule> _rules = rules.ToArray();
    private readonly BloodWatchDbContext _dbContext = dbContext;
    private readonly DispatchEngine _dispatchEngine = dispatchEngine;
    private readonly ILogger<FetchPortugalReservesJob> _logger = logger;

    public async Task<FetchPortugalReservesResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var adapter = _adapters.FirstOrDefault(candidate => candidate.AdapterKey == PortugalAdapter.DefaultAdapterKey)
            ?? throw new InvalidOperationException(
                $"No adapter registered for {PortugalAdapter.DefaultAdapterKey}.");

        var snapshot = await adapter.FetchLatestAsync(cancellationToken);

        var polledAtUtc = DateTime.UtcNow;
        var source = await EnsureSourceAsync(snapshot.Source, polledAtUtc, cancellationToken);
        source.LastPolledAtUtc = polledAtUtc;

        await EnsureRegionsAsync(source.Id, snapshot.Items, polledAtUtc, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var regionRows = await _dbContext.Regions
            .Where(region => region.SourceId == source.Id)
            .ToListAsync(cancellationToken);

        var regionIdByKey = regionRows.ToDictionary(region => region.Key, region => region.Id, StringComparer.Ordinal);
        var regionById = regionRows.ToDictionary(
            region => region.Id,
            region => new RegionRef(region.Key, region.DisplayName));

        var incomingByKey = BuildIncomingReserves(snapshot, regionIdByKey);

        var existingRows = await _dbContext.CurrentReserves
            .Where(entry => entry.SourceId == source.Id)
            .ToListAsync(cancellationToken);

        var existingByKey = existingRows.ToDictionary(
            entry => new CurrentReserveKey(entry.RegionId, entry.MetricKey),
            entry => entry);

        var previousRows = existingRows
            .Select(entry => new SnapshotReserveRow(
                entry.RegionId,
                entry.MetricKey,
                entry.Value,
                entry.Unit,
                entry.Severity))
            .ToArray();

        Snapshot? previousSnapshot = previousRows.Length == 0
            ? null
            : BuildSnapshot(snapshot.Source, snapshot.CapturedAtUtc, snapshot.ReferenceDate, previousRows, regionById);

        var insertedCount = 0;
        var updatedCount = 0;
        var matchedExistingCount = 0;

        var currentByKey = new Dictionary<CurrentReserveKey, CurrentReserveEntity>(existingByKey);

        foreach (var incoming in incomingByKey.Values)
        {
            if (existingByKey.TryGetValue(incoming.Key, out var existing))
            {
                matchedExistingCount++;
                existing.Value = incoming.Value;
                existing.Unit = incoming.Unit;
                existing.Severity = incoming.Severity;
                existing.ReferenceDate = snapshot.ReferenceDate;
                existing.CapturedAtUtc = snapshot.CapturedAtUtc;
                existing.UpdatedAtUtc = polledAtUtc;
                updatedCount++;
                continue;
            }

            var entity = new CurrentReserveEntity
            {
                Id = Guid.NewGuid(),
                SourceId = source.Id,
                RegionId = incoming.Key.RegionId,
                MetricKey = incoming.Key.MetricKey,
                Value = incoming.Value,
                Unit = incoming.Unit,
                Severity = incoming.Severity,
                ReferenceDate = snapshot.ReferenceDate,
                CapturedAtUtc = snapshot.CapturedAtUtc,
                UpdatedAtUtc = polledAtUtc,
            };

            _dbContext.CurrentReserves.Add(entity);
            currentByKey[incoming.Key] = entity;
            insertedCount++;
        }

        var carriedForwardCount = Math.Max(0, existingByKey.Count - matchedExistingCount);

        var currentRows = currentByKey.Values
            .Select(entry => new SnapshotReserveRow(
                entry.RegionId,
                entry.MetricKey,
                entry.Value,
                entry.Unit,
                entry.Severity))
            .ToArray();

        var currentSnapshot = BuildSnapshot(snapshot.Source, snapshot.CapturedAtUtc, snapshot.ReferenceDate, currentRows, regionById);

        var generatedEvents = await EvaluateRulesAsync(previousSnapshot, currentSnapshot, cancellationToken);
        var insertedEvents = await PersistEventsAsync(
            source.Id,
            generatedEvents,
            regionIdByKey,
            currentByKey,
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (insertedEvents.Count > 0)
        {
            var sentCount = await _dispatchEngine.DispatchAsync(insertedEvents, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Dispatched {SentCount} deliveries for {EventCount} newly created events.",
                sentCount,
                insertedEvents.Count);
        }

        return new FetchPortugalReservesResult(
            insertedCount,
            updatedCount,
            carriedForwardCount,
            polledAtUtc);
    }

    private async Task<SourceEntity> EnsureSourceAsync(SourceRef sourceRef, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var source = await _dbContext.Sources
            .SingleOrDefaultAsync(entry => entry.AdapterKey == sourceRef.AdapterKey, cancellationToken);

        if (source is not null)
        {
            return source;
        }

        source = new SourceEntity
        {
            Id = Guid.NewGuid(),
            AdapterKey = sourceRef.AdapterKey,
            Name = sourceRef.Name,
            CreatedAtUtc = nowUtc,
            LastPolledAtUtc = nowUtc,
        };

        _dbContext.Sources.Add(source);

        return source;
    }

    private async Task EnsureRegionsAsync(
        Guid sourceId,
        IEnumerable<SnapshotItem> snapshotItems,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var desiredRegionsByKey = snapshotItems
            .Select(item => item.Region)
            .GroupBy(region => region.Key, StringComparer.Ordinal)
            .ToDictionary(
                grouping => grouping.Key,
                grouping => grouping.First().DisplayName,
                StringComparer.Ordinal);

        if (desiredRegionsByKey.Count == 0)
        {
            return;
        }

        var regionKeys = desiredRegionsByKey.Keys.ToArray();
        var existingRegions = await _dbContext.Regions
            .Where(region => region.SourceId == sourceId && regionKeys.Contains(region.Key))
            .ToListAsync(cancellationToken);

        var existingByKey = existingRegions.ToDictionary(region => region.Key, region => region, StringComparer.Ordinal);

        var hasNewRegion = false;
        foreach (var desiredRegion in desiredRegionsByKey)
        {
            if (existingByKey.ContainsKey(desiredRegion.Key))
            {
                continue;
            }

            var entity = new RegionEntity
            {
                Id = Guid.NewGuid(),
                SourceId = sourceId,
                Key = desiredRegion.Key,
                DisplayName = desiredRegion.Value,
                CreatedAtUtc = nowUtc,
            };

            _dbContext.Regions.Add(entity);
            existingByKey[desiredRegion.Key] = entity;
            hasNewRegion = true;
        }

        if (hasNewRegion)
        {
            _logger.LogInformation(
                "Detected {NewRegions} new regions for source {SourceId}.",
                desiredRegionsByKey.Count - existingRegions.Count,
                sourceId);
        }
    }

    private async Task<IReadOnlyCollection<Event>> EvaluateRulesAsync(
        Snapshot? previousSnapshot,
        Snapshot currentSnapshot,
        CancellationToken cancellationToken)
    {
        if (_rules.Count == 0)
        {
            return [];
        }

        var events = new List<Event>();
        foreach (var rule in _rules)
        {
            var ruleEvents = await rule.EvaluateAsync(previousSnapshot, currentSnapshot, cancellationToken);
            if (ruleEvents.Count == 0)
            {
                continue;
            }

            events.AddRange(ruleEvents);
        }

        return events;
    }

    private async Task<IReadOnlyCollection<EventEntity>> PersistEventsAsync(
        Guid sourceId,
        IReadOnlyCollection<Event> generatedEvents,
        IReadOnlyDictionary<string, Guid> regionIdByKey,
        IReadOnlyDictionary<CurrentReserveKey, CurrentReserveEntity> currentByKey,
        CancellationToken cancellationToken)
    {
        if (generatedEvents.Count == 0)
        {
            return [];
        }

        var pending = new List<PendingEvent>();
        foreach (var generatedEvent in generatedEvents)
        {
            if (generatedEvent.Region is null)
            {
                continue;
            }

            if (!regionIdByKey.TryGetValue(generatedEvent.Region.Key, out var regionId))
            {
                continue;
            }

            var reserveKey = new CurrentReserveKey(regionId, generatedEvent.Metric.Key);
            if (!currentByKey.TryGetValue(reserveKey, out var reserve))
            {
                continue;
            }

            var idempotencyKey = ComputeIdempotencyKey(generatedEvent);
            pending.Add(new PendingEvent(generatedEvent, idempotencyKey, regionId, reserve.Id));
        }

        if (pending.Count == 0)
        {
            return [];
        }

        var uniquePending = pending
            .GroupBy(entry => entry.IdempotencyKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();

        var candidateKeys = uniquePending
            .Select(entry => entry.IdempotencyKey)
            .ToArray();

        var existingKeys = await _dbContext.Events
            .AsNoTracking()
            .Where(entry => candidateKeys.Contains(entry.IdempotencyKey))
            .Select(entry => entry.IdempotencyKey)
            .ToListAsync(cancellationToken);

        var existingKeySet = existingKeys.ToHashSet(StringComparer.Ordinal);

        var insertedEvents = new List<EventEntity>();
        foreach (var pendingEvent in uniquePending)
        {
            if (existingKeySet.Contains(pendingEvent.IdempotencyKey))
            {
                continue;
            }

            var entity = new EventEntity
            {
                Id = Guid.NewGuid(),
                SourceId = sourceId,
                CurrentReserveId = pendingEvent.CurrentReserveId,
                RegionId = pendingEvent.RegionId,
                RuleKey = pendingEvent.Event.RuleKey,
                MetricKey = pendingEvent.Event.Metric.Key,
                IdempotencyKey = pendingEvent.IdempotencyKey,
                PayloadJson = pendingEvent.Event.PayloadJson,
                CreatedAtUtc = pendingEvent.Event.CreatedAtUtc,
            };

            _dbContext.Events.Add(entity);
            insertedEvents.Add(entity);
        }

        return insertedEvents;
    }

    private static Snapshot BuildSnapshot(
        SourceRef source,
        DateTime capturedAtUtc,
        DateOnly? referenceDate,
        IEnumerable<SnapshotReserveRow> rows,
        IReadOnlyDictionary<Guid, RegionRef> regionById)
    {
        var items = rows
            .Select(row =>
            {
                if (!regionById.TryGetValue(row.RegionId, out var region))
                {
                    return null;
                }

                return new SnapshotItem(
                    new Metric(row.MetricKey, row.MetricKey, row.Unit),
                    region,
                    row.Value,
                    row.Unit,
                    row.Severity);
            })
            .Where(item => item is not null)
            .Cast<SnapshotItem>()
            .OrderBy(item => item.Region.Key, StringComparer.Ordinal)
            .ThenBy(item => item.Metric.Key, StringComparer.Ordinal)
            .ToArray();

        return new Snapshot(source, capturedAtUtc, referenceDate, items);
    }

    private static string ComputeIdempotencyKey(Event @event)
    {
        var idempotencySeed = ExtractIdempotencySeed(@event.PayloadJson);
        var builder = new StringBuilder();
        builder
            .Append(@event.RuleKey).Append('|')
            .Append(@event.Source.AdapterKey).Append('|')
            .Append(@event.Region?.Key ?? "global").Append('|')
            .Append(@event.Metric.Key).Append('|')
            .Append(idempotencySeed.Signal).Append('|')
            .Append(idempotencySeed.TransitionKind).Append('|')
            .Append(idempotencySeed.CurrentState).Append('|')
            .Append(idempotencySeed.CurrentCriticalBucket?.ToString() ?? "none").Append('|')
            .Append(idempotencySeed.CurrentUnits?.ToString("G29", CultureInfo.InvariantCulture) ?? "none").Append('|')
            .Append(idempotencySeed.CapturedAtUtc ?? "none");

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static IdempotencySeed ExtractIdempotencySeed(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;

            var signal = TryReadString(root, "signal") ?? "unknown";
            var transitionKind = TryReadString(root, "transitionKind") ?? "unknown";
            var currentState = TryReadString(root, "currentState") ?? "unknown";
            var currentCriticalBucket = TryReadInt(root, "currentCriticalBucket");
            var currentUnits = TryReadDecimal(root, "currentUnits");
            var capturedAtUtc = TryReadString(root, "capturedAtUtc");

            return new IdempotencySeed(
                Signal: signal,
                TransitionKind: transitionKind,
                CurrentState: currentState,
                CurrentCriticalBucket: currentCriticalBucket,
                CurrentUnits: currentUnits,
                CapturedAtUtc: capturedAtUtc);
        }
        catch (JsonException)
        {
            return new IdempotencySeed(
                Signal: "unknown",
                TransitionKind: "unknown",
                CurrentState: "unknown",
                CurrentCriticalBucket: null,
                CurrentUnits: null,
                CapturedAtUtc: null);
        }
    }

    private static string? TryReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static int? TryReadInt(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var intValue))
        {
            return intValue;
        }

        if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static decimal? TryReadDecimal(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var decimalValue))
        {
            return decimalValue;
        }

        if (property.ValueKind == JsonValueKind.String
            && decimal.TryParse(property.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static Dictionary<CurrentReserveKey, IncomingCurrentReserve> BuildIncomingReserves(
        Snapshot snapshot,
        IReadOnlyDictionary<string, Guid> regionsByKey)
    {
        return snapshot.Items
            .Select(item =>
            {
                if (!regionsByKey.TryGetValue(item.Region.Key, out var regionId))
                {
                    return null;
                }

                return new IncomingCurrentReserve(
                    new CurrentReserveKey(regionId, item.Metric.Key),
                    item.Value,
                    item.Unit,
                    item.Severity);
            })
            .Where(item => item is not null)
            .Cast<IncomingCurrentReserve>()
            .GroupBy(item => item.Key)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var first = group.First();
                    var severity = group
                        .Select(item => item.Severity)
                        .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

                    return new IncomingCurrentReserve(
                        first.Key,
                        group.Sum(item => item.Value),
                        first.Unit,
                        severity);
                });
    }

    private sealed record PendingEvent(Event Event, string IdempotencyKey, Guid RegionId, Guid CurrentReserveId);

    private sealed record IdempotencySeed(
        string Signal,
        string TransitionKind,
        string CurrentState,
        int? CurrentCriticalBucket,
        decimal? CurrentUnits,
        string? CapturedAtUtc);

    private sealed record CurrentReserveKey(Guid RegionId, string MetricKey);

    private sealed record IncomingCurrentReserve(
        CurrentReserveKey Key,
        decimal Value,
        string Unit,
        string? Severity);

    private sealed record SnapshotReserveRow(
        Guid RegionId,
        string MetricKey,
        decimal Value,
        string Unit,
        string? Severity);
}
