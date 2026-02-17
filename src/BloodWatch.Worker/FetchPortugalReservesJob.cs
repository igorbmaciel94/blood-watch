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
        var snapshotHash = SnapshotHashCalculator.Compute(snapshot);

        var source = await EnsureSourceAsync(snapshot.Source, cancellationToken);
        if (await SnapshotAlreadyExistsAsync(source.Id, snapshotHash, cancellationToken))
        {
            return new FetchPortugalReservesResult(0, 1, 0);
        }

        var nowUtc = DateTime.UtcNow;
        var regionsByKey = await EnsureRegionsAsync(source.Id, snapshot.Items, nowUtc, cancellationToken);
        var snapshotEntity = BuildSnapshotEntity(snapshot, source.Id, snapshotHash, regionsByKey, nowUtc);

        _dbContext.Snapshots.Add(snapshotEntity);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new FetchPortugalReservesResult(1, 0, snapshotEntity.Items.Count);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(
                ex,
                "Snapshot persistence failed for adapter {AdapterKey}. Checking duplicate hash fallback.",
                snapshot.Source.AdapterKey);

            _dbContext.ChangeTracker.Clear();
            if (await SnapshotAlreadyExistsAsync(source.Id, snapshotHash, cancellationToken))
            {
                return new FetchPortugalReservesResult(0, 1, 0);
            }

            throw;
        }
    }

    private async Task<SourceEntity> EnsureSourceAsync(SourceRef sourceRef, CancellationToken cancellationToken)
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
            CreatedAtUtc = DateTime.UtcNow,
        };

        _dbContext.Sources.Add(source);
        await _dbContext.SaveChangesAsync(cancellationToken);

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
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return existingByKey.ToDictionary(region => region.Key, region => region.Value.Id, StringComparer.Ordinal);
    }

    private static SnapshotEntity BuildSnapshotEntity(
        Snapshot snapshot,
        Guid sourceId,
        string hash,
        IReadOnlyDictionary<string, Guid> regionsByKey,
        DateTime nowUtc)
    {
        var snapshotEntity = new SnapshotEntity
        {
            Id = Guid.NewGuid(),
            SourceId = sourceId,
            CapturedAtUtc = snapshot.CapturedAtUtc,
            ReferenceDate = snapshot.ReferenceDate,
            Hash = hash,
            CreatedAtUtc = nowUtc,
        };

        foreach (var item in snapshot.Items)
        {
            if (!regionsByKey.TryGetValue(item.Region.Key, out var regionId))
            {
                continue;
            }

            snapshotEntity.Items.Add(new SnapshotItemEntity
            {
                Id = Guid.NewGuid(),
                SnapshotId = snapshotEntity.Id,
                RegionId = regionId,
                MetricKey = item.Metric.Key,
                Value = item.Value,
                Unit = item.Unit,
                Severity = item.Severity,
                CreatedAtUtc = nowUtc,
            });
        }

        return snapshotEntity;
    }

    private Task<bool> SnapshotAlreadyExistsAsync(Guid sourceId, string hash, CancellationToken cancellationToken)
    {
        return _dbContext.Snapshots
            .AsNoTracking()
            .AnyAsync(entry => entry.SourceId == sourceId && entry.Hash == hash, cancellationToken);
    }
}
