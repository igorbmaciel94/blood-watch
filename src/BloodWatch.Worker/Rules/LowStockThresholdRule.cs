using System.Text.Json;
using BloodWatch.Core.Contracts;
using BloodWatch.Core.Models;
using BloodWatch.Worker.Alerts;

namespace BloodWatch.Worker.Rules;

public sealed class LowStockThresholdRule(AlertThresholdProfileResolver thresholdProfileResolver) : IRule
{
    public const string DefaultRuleKey = "low-stock-threshold.v1";

    private readonly AlertThresholdProfileResolver _thresholdProfileResolver = thresholdProfileResolver;

    public string RuleKey => DefaultRuleKey;

    public Task<IReadOnlyCollection<Event>> EvaluateAsync(
        Snapshot? previousSnapshot,
        Snapshot currentSnapshot,
        CancellationToken cancellationToken = default)
    {
        var previousByKey = (previousSnapshot?.Items ?? [])
            .GroupBy(item => BuildKey(item.Region.Key, item.Metric.Key), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);

        var events = new List<Event>();
        foreach (var currentItem in currentSnapshot.Items
                     .OrderBy(item => item.Region.Key, StringComparer.Ordinal)
                     .ThenBy(item => item.Metric.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var itemKey = BuildKey(currentItem.Region.Key, currentItem.Metric.Key);
            var hasPrevious = previousByKey.TryGetValue(itemKey, out var previousItem);
            var profile = _thresholdProfileResolver.Resolve(currentItem.Metric.Key);

            var previousState = hasPrevious
                ? ClassifyState(previousItem!.Value, profile)
                : new StockState(StockStateKind.Normal, null);

            var currentState = ClassifyState(currentItem.Value, profile);
            var signal = ResolveSignal(hasPrevious, previousState, currentState);
            if (signal is null)
            {
                continue;
            }

            var transitionKind = ResolveTransitionKind(hasPrevious, previousState, currentState);
            var createdAtUtc = DateTime.UtcNow;
            var payloadJson = JsonSerializer.Serialize(new
            {
                source = currentSnapshot.Source.AdapterKey,
                region = currentItem.Region.Key,
                metric = currentItem.Metric.Key,
                signal,
                transitionKind,
                previousUnits = hasPrevious ? (decimal?)previousItem!.Value : null,
                currentUnits = currentItem.Value,
                criticalUnits = profile.CriticalUnits,
                warningUnits = profile.WarningUnits,
                stepDownUnits = profile.StepDownUnits,
                previousState = previousState.Kind.ToString().ToLowerInvariant(),
                currentState = currentState.Kind.ToString().ToLowerInvariant(),
                previousCriticalBucket = previousState.CriticalBucket,
                currentCriticalBucket = currentState.CriticalBucket,
                capturedAtUtc = currentSnapshot.CapturedAtUtc,
                referenceDate = currentSnapshot.ReferenceDate,
            });

            events.Add(new Event(
                RuleKey,
                currentSnapshot.Source,
                currentItem.Metric,
                currentItem.Region,
                createdAtUtc,
                payloadJson));
        }

        return Task.FromResult<IReadOnlyCollection<Event>>(events);
    }

    private static string BuildKey(string regionKey, string metricKey)
    {
        return $"{regionKey}|{metricKey}";
    }

    private static StockState ClassifyState(decimal value, AlertThresholdProfile profile)
    {
        if (value > profile.WarningUnits)
        {
            return new StockState(StockStateKind.Normal, null);
        }

        if (value > profile.CriticalUnits)
        {
            return new StockState(StockStateKind.Warning, null);
        }

        var delta = profile.CriticalUnits - value;
        var bucket = (int)Math.Floor(delta / profile.StepDownUnits);
        if (bucket < 0)
        {
            bucket = 0;
        }

        return new StockState(StockStateKind.Critical, bucket);
    }

    private static string? ResolveSignal(bool hasPrevious, StockState previousState, StockState currentState)
    {
        if (currentState.Kind == StockStateKind.Critical)
        {
            return "critical-active";
        }

        if (hasPrevious && previousState.Kind == StockStateKind.Critical && currentState.Kind != StockStateKind.Critical)
        {
            return "recovery";
        }

        return null;
    }

    private static string ResolveTransitionKind(bool hasPrevious, StockState previousState, StockState currentState)
    {
        if (currentState.Kind == StockStateKind.Critical)
        {
            if (!hasPrevious)
            {
                return "initial-critical";
            }

            return previousState.Kind == StockStateKind.Critical
                ? "still-critical"
                : "entered-critical";
        }

        if (previousState.Kind == StockStateKind.Critical && currentState.Kind != StockStateKind.Critical)
        {
            return "recovered-from-critical";
        }

        return "state-unchanged";
    }

    private enum StockStateKind
    {
        Normal = 0,
        Warning = 1,
        Critical = 2,
    }

    private readonly record struct StockState(StockStateKind Kind, int? CriticalBucket);
}
