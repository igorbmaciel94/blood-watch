using BloodWatch.Api.Contracts;
using BloodWatch.Core.Models;
using BloodWatch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BloodWatch.Api.Services;

public sealed class ReserveAnalyticsQueryService(BloodWatchDbContext dbContext) : IReserveAnalyticsQueryService
{
    private readonly BloodWatchDbContext _dbContext = dbContext;

    public async Task<ReserveDeltasResponse?> GetReserveDeltasAsync(
        Guid sourceId,
        string sourceKey,
        int limit,
        CancellationToken cancellationToken)
    {
        var latestReferenceDates = await _dbContext.ReserveHistoryObservations
            .AsNoTracking()
            .Where(entry => entry.SourceId == sourceId)
            .Select(entry => entry.ReferenceDate)
            .Distinct()
            .OrderByDescending(entry => entry)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (latestReferenceDates.Count < 2)
        {
            return null;
        }

        var currentReferenceDate = latestReferenceDates[0];
        var previousReferenceDate = latestReferenceDates[1];

        var rows = await _dbContext.ReserveHistoryObservations
            .AsNoTracking()
            .Where(entry =>
                entry.SourceId == sourceId
                && (entry.ReferenceDate == currentReferenceDate || entry.ReferenceDate == previousReferenceDate))
            .Select(entry => new HistoryRow(
                entry.RegionId,
                entry.MetricKey,
                entry.StatusKey,
                entry.StatusRank,
                entry.ReferenceDate,
                entry.CapturedAtUtc))
            .ToListAsync(cancellationToken);

        var weeklyStates = BuildWeeklyStates(rows);
        var currentByKey = weeklyStates
            .Where(entry => entry.ReferenceDate == currentReferenceDate)
            .ToDictionary(entry => new MetricStateKey(entry.RegionId, entry.MetricKey), entry => entry);

        var previousByKey = weeklyStates
            .Where(entry => entry.ReferenceDate == previousReferenceDate)
            .ToDictionary(entry => new MetricStateKey(entry.RegionId, entry.MetricKey), entry => entry);

        var changedRows = currentByKey
            .Select(pair =>
            {
                if (!previousByKey.TryGetValue(pair.Key, out var previous))
                {
                    return null;
                }

                var current = pair.Value;
                if (string.Equals(previous.StatusKey, current.StatusKey, StringComparison.Ordinal)
                    && previous.StatusRank == current.StatusRank)
                {
                    return null;
                }

                return new DeltaProjection(
                    current.RegionId,
                    current.MetricKey,
                    previous.StatusKey,
                    previous.StatusRank,
                    current.StatusKey,
                    current.StatusRank);
            })
            .Where(entry => entry is not null)
            .Cast<DeltaProjection>()
            .ToArray();

        var regionMap = await LoadRegionItemsAsync(
            sourceId,
            changedRows.Select(entry => entry.RegionId),
            cancellationToken);

        var items = changedRows
            .Select(entry =>
            {
                if (!regionMap.TryGetValue(entry.RegionId, out var region))
                {
                    return null;
                }

                var rankDelta = (short)(entry.CurrentStatusRank - entry.PreviousStatusRank);
                return new ReserveDeltaItem(
                    region,
                    entry.MetricKey,
                    entry.PreviousStatusKey,
                    entry.PreviousStatusRank,
                    entry.CurrentStatusKey,
                    entry.CurrentStatusRank,
                    rankDelta);
            })
            .Where(entry => entry is not null)
            .Cast<ReserveDeltaItem>()
            .OrderByDescending(entry => Math.Abs(entry.RankDelta))
            .ThenBy(entry => entry.Region.Key, StringComparer.Ordinal)
            .ThenBy(entry => entry.Metric, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();

        return new ReserveDeltasResponse(sourceKey, currentReferenceDate, previousReferenceDate, items);
    }

    public async Task<TopDowngradesResponse?> GetTopDowngradesAsync(
        Guid sourceId,
        string sourceKey,
        int weeks,
        int limit,
        CancellationToken cancellationToken)
    {
        var window = await BuildWindowAsync(sourceId, weeks, cancellationToken);
        if (window is null)
        {
            return null;
        }

        var weeklyStates = await LoadWeeklyStatesInWindowAsync(sourceId, window, cancellationToken);

        var downgradeCountsByRegion = weeklyStates
            .GroupBy(entry => new MetricStateKey(entry.RegionId, entry.MetricKey))
            .SelectMany(group =>
            {
                var ordered = group.OrderBy(entry => entry.ReferenceDate).ToArray();
                if (ordered.Length <= 1)
                {
                    return Array.Empty<RegionDowngradeProjection>();
                }

                var downgrades = 0;
                for (var index = 1; index < ordered.Length; index++)
                {
                    if (ordered[index].StatusRank > ordered[index - 1].StatusRank)
                    {
                        downgrades++;
                    }
                }

                return downgrades == 0
                    ? Array.Empty<RegionDowngradeProjection>()
                    : [new RegionDowngradeProjection(ordered[0].RegionId, downgrades)];
            })
            .GroupBy(entry => entry.RegionId)
            .Select(group => new RegionDowngradeProjection(group.Key, group.Sum(entry => entry.Downgrades)))
            .OrderByDescending(entry => entry.Downgrades)
            .ToArray();

        var regionMap = await LoadRegionItemsAsync(
            sourceId,
            downgradeCountsByRegion.Select(entry => entry.RegionId),
            cancellationToken);

        var items = downgradeCountsByRegion
            .Select(entry =>
            {
                if (!regionMap.TryGetValue(entry.RegionId, out var region))
                {
                    return null;
                }

                return new TopDowngradeItem(region, entry.Downgrades);
            })
            .Where(entry => entry is not null)
            .Cast<TopDowngradeItem>()
            .OrderByDescending(entry => entry.Downgrades)
            .ThenBy(entry => entry.Region.Key, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();

        return new TopDowngradesResponse(sourceKey, weeks, window.FromReferenceDate, window.ToReferenceDate, items);
    }

    public async Task<TimeInStatusResponse?> GetTimeInStatusAsync(
        Guid sourceId,
        string sourceKey,
        int weeks,
        int limit,
        CancellationToken cancellationToken)
    {
        var window = await BuildWindowAsync(sourceId, weeks, cancellationToken);
        if (window is null)
        {
            return null;
        }

        var weeklyStates = await LoadWeeklyStatesInWindowAsync(sourceId, window, cancellationToken);

        var aggregatedRows = weeklyStates
            .GroupBy(entry => new MetricStateKey(entry.RegionId, entry.MetricKey))
            .Select(group =>
            {
                var rows = group.ToArray();
                return new TimeInStatusProjection(
                    group.Key.RegionId,
                    group.Key.MetricKey,
                    rows.Count(entry => string.Equals(entry.StatusKey, ReserveStatusCatalog.Watch, StringComparison.Ordinal)),
                    rows.Count(entry => string.Equals(entry.StatusKey, ReserveStatusCatalog.Warning, StringComparison.Ordinal)),
                    rows.Count(entry => string.Equals(entry.StatusKey, ReserveStatusCatalog.Critical, StringComparison.Ordinal)),
                    rows.Length);
            })
            .Where(entry => entry.WatchWeeks > 0 || entry.WarningWeeks > 0 || entry.CriticalWeeks > 0)
            .ToArray();

        var regionMap = await LoadRegionItemsAsync(
            sourceId,
            aggregatedRows.Select(entry => entry.RegionId),
            cancellationToken);

        var items = aggregatedRows
            .Select(entry =>
            {
                if (!regionMap.TryGetValue(entry.RegionId, out var region))
                {
                    return null;
                }

                return new TimeInStatusItem(
                    region,
                    entry.MetricKey,
                    entry.WatchWeeks,
                    entry.WarningWeeks,
                    entry.CriticalWeeks,
                    entry.TotalObservedWeeks);
            })
            .Where(entry => entry is not null)
            .Cast<TimeInStatusItem>()
            .OrderByDescending(entry => entry.CriticalWeeks)
            .ThenByDescending(entry => entry.WarningWeeks)
            .ThenByDescending(entry => entry.WatchWeeks)
            .ThenBy(entry => entry.Region.Key, StringComparer.Ordinal)
            .ThenBy(entry => entry.Metric, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();

        return new TimeInStatusResponse(sourceKey, weeks, window.FromReferenceDate, window.ToReferenceDate, items);
    }

    public async Task<UnstableMetricsResponse?> GetUnstableMetricsAsync(
        Guid sourceId,
        string sourceKey,
        int weeks,
        int limit,
        CancellationToken cancellationToken)
    {
        var window = await BuildWindowAsync(sourceId, weeks, cancellationToken);
        if (window is null)
        {
            return null;
        }

        var weeklyStates = await LoadWeeklyStatesInWindowAsync(sourceId, window, cancellationToken);

        var transitionCounts = weeklyStates
            .GroupBy(entry => new MetricStateKey(entry.RegionId, entry.MetricKey))
            .Select(group =>
            {
                var ordered = group.OrderBy(entry => entry.ReferenceDate).ToArray();
                if (ordered.Length <= 1)
                {
                    return null;
                }

                var transitions = 0;
                for (var index = 1; index < ordered.Length; index++)
                {
                    if (!string.Equals(ordered[index].StatusKey, ordered[index - 1].StatusKey, StringComparison.Ordinal))
                    {
                        transitions++;
                    }
                }

                return transitions == 0
                    ? null
                    : new UnstableMetricProjection(group.Key.RegionId, group.Key.MetricKey, transitions);
            })
            .Where(entry => entry is not null)
            .Cast<UnstableMetricProjection>()
            .ToArray();

        var regionMap = await LoadRegionItemsAsync(
            sourceId,
            transitionCounts.Select(entry => entry.RegionId),
            cancellationToken);

        var items = transitionCounts
            .Select(entry =>
            {
                if (!regionMap.TryGetValue(entry.RegionId, out var region))
                {
                    return null;
                }

                return new UnstableMetricItem(region, entry.MetricKey, entry.Transitions);
            })
            .Where(entry => entry is not null)
            .Cast<UnstableMetricItem>()
            .OrderByDescending(entry => entry.Transitions)
            .ThenBy(entry => entry.Region.Key, StringComparer.Ordinal)
            .ThenBy(entry => entry.Metric, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();

        return new UnstableMetricsResponse(sourceKey, weeks, window.FromReferenceDate, window.ToReferenceDate, items);
    }

    private async Task<IReadOnlyCollection<WeeklyState>> LoadWeeklyStatesInWindowAsync(
        Guid sourceId,
        ReferenceWindow window,
        CancellationToken cancellationToken)
    {
        var rows = await _dbContext.ReserveHistoryObservations
            .AsNoTracking()
            .Where(entry =>
                entry.SourceId == sourceId
                && entry.ReferenceDate >= window.FromReferenceDate
                && entry.ReferenceDate <= window.ToReferenceDate)
            .Select(entry => new HistoryRow(
                entry.RegionId,
                entry.MetricKey,
                entry.StatusKey,
                entry.StatusRank,
                entry.ReferenceDate,
                entry.CapturedAtUtc))
            .ToListAsync(cancellationToken);

        return BuildWeeklyStates(rows);
    }

    private static IReadOnlyCollection<WeeklyState> BuildWeeklyStates(IReadOnlyCollection<HistoryRow> rows)
    {
        return rows
            .GroupBy(entry => new WeeklyStateKey(entry.RegionId, entry.MetricKey, entry.ReferenceDate))
            .Select(group =>
            {
                var latest = group
                    .OrderByDescending(entry => entry.CapturedAtUtc)
                    .ThenByDescending(entry => entry.StatusRank)
                    .ThenBy(entry => entry.StatusKey, StringComparer.Ordinal)
                    .First();

                return new WeeklyState(
                    latest.RegionId,
                    latest.MetricKey,
                    latest.ReferenceDate,
                    latest.StatusKey,
                    latest.StatusRank);
            })
            .ToArray();
    }

    private async Task<ReferenceWindow?> BuildWindowAsync(
        Guid sourceId,
        int weeks,
        CancellationToken cancellationToken)
    {
        var latestReferenceDate = await _dbContext.ReserveHistoryObservations
            .AsNoTracking()
            .Where(entry => entry.SourceId == sourceId)
            .Select(entry => (DateOnly?)entry.ReferenceDate)
            .MaxAsync(cancellationToken);

        if (latestReferenceDate is null)
        {
            return null;
        }

        var fromReferenceDate = latestReferenceDate.Value.AddDays(-7 * (weeks - 1));
        return new ReferenceWindow(fromReferenceDate, latestReferenceDate.Value);
    }

    private async Task<Dictionary<Guid, RegionItem>> LoadRegionItemsAsync(
        Guid sourceId,
        IEnumerable<Guid> regionIds,
        CancellationToken cancellationToken)
    {
        var uniqueRegionIds = regionIds.Distinct().ToArray();
        if (uniqueRegionIds.Length == 0)
        {
            return [];
        }

        return await _dbContext.Regions
            .AsNoTracking()
            .Where(region => region.SourceId == sourceId && uniqueRegionIds.Contains(region.Id))
            .Select(region => new
            {
                region.Id,
                region.Key,
                region.DisplayName,
            })
            .ToDictionaryAsync(
                row => row.Id,
                row => new RegionItem(row.Key, row.DisplayName),
                cancellationToken);
    }

    private sealed record HistoryRow(
        Guid RegionId,
        string MetricKey,
        string StatusKey,
        short StatusRank,
        DateOnly ReferenceDate,
        DateTime CapturedAtUtc);

    private sealed record WeeklyState(
        Guid RegionId,
        string MetricKey,
        DateOnly ReferenceDate,
        string StatusKey,
        short StatusRank);

    private sealed record WeeklyStateKey(Guid RegionId, string MetricKey, DateOnly ReferenceDate);
    private sealed record MetricStateKey(Guid RegionId, string MetricKey);
    private sealed record DeltaProjection(
        Guid RegionId,
        string MetricKey,
        string PreviousStatusKey,
        short PreviousStatusRank,
        string CurrentStatusKey,
        short CurrentStatusRank);

    private sealed record RegionDowngradeProjection(Guid RegionId, int Downgrades);
    private sealed record TimeInStatusProjection(
        Guid RegionId,
        string MetricKey,
        int WatchWeeks,
        int WarningWeeks,
        int CriticalWeeks,
        int TotalObservedWeeks);

    private sealed record UnstableMetricProjection(Guid RegionId, string MetricKey, int Transitions);
    private sealed record ReferenceWindow(DateOnly FromReferenceDate, DateOnly ToReferenceDate);
}
