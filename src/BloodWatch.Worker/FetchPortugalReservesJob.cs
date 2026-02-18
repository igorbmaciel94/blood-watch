using BloodWatch.Adapters.Portugal;
using BloodWatch.Core.Contracts;
using BloodWatch.Core.Models;
using BloodWatch.Infrastructure.Persistence;
using BloodWatch.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BloodWatch.Worker;

public sealed class FetchPortugalReservesJob(
    IEnumerable<IDataSourceAdapter> adapters,
    BloodWatchDbContext dbContext,
    ILogger<FetchPortugalReservesJob> logger)
{
    private readonly IReadOnlyCollection<IDataSourceAdapter> _adapters = adapters.ToArray();
    private readonly BloodWatchDbContext _dbContext = dbContext;
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

        var regionsByKey = await EnsureRegionsAsync(source.Id, snapshot.Items, polledAtUtc, cancellationToken);
        var incomingByKey = BuildIncomingReserves(snapshot, regionsByKey);
        var existingRows = await _dbContext.CurrentReserves
            .Where(entry => entry.SourceId == source.Id)
            .ToListAsync(cancellationToken);

        var existingByKey = existingRows.ToDictionary(
            entry => new CurrentReserveKey(entry.RegionId, entry.MetricKey),
            entry => entry);

        var insertedCount = 0;
        var updatedCount = 0;
        var matchedExistingCount = 0;

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

            _dbContext.CurrentReserves.Add(new CurrentReserveEntity
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
            });

            insertedCount++;
        }

        var carriedForwardCount = Math.Max(0, existingByKey.Count - matchedExistingCount);
        await _dbContext.SaveChangesAsync(cancellationToken);

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

    private async Task<Dictionary<string, Guid>> EnsureRegionsAsync(
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
            return [];
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

        return existingByKey.ToDictionary(region => region.Key, region => region.Value.Id, StringComparer.Ordinal);
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

    private sealed record CurrentReserveKey(Guid RegionId, string MetricKey);

    private sealed record IncomingCurrentReserve(
        CurrentReserveKey Key,
        decimal Value,
        string Unit,
        string? Severity);
}
