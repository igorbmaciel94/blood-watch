using System.Text.Json;
using BloodWatch.Core.Contracts;
using BloodWatch.Core.Models;

namespace BloodWatch.Worker.Rules;

public sealed class StatusTransitionRule : IRule
{
    public const string DefaultRuleKey = "reserve-status-transition.v1";

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

            var key = BuildKey(currentItem.Region.Key, currentItem.Metric.Key);
            var hasPrevious = previousByKey.TryGetValue(key, out var previousItem);

            var previousStatusKey = hasPrevious
                ? ReserveStatusCatalog.NormalizeKey(previousItem!.StatusKey)
                : ReserveStatusCatalog.Normal;

            var currentStatusKey = ReserveStatusCatalog.NormalizeKey(currentItem.StatusKey);
            var signal = ResolveSignal(previousStatusKey, currentStatusKey);
            if (signal is null)
            {
                continue;
            }

            var transitionKind = ResolveTransitionKind(previousStatusKey, currentStatusKey);

            var payloadJson = JsonSerializer.Serialize(new
            {
                source = currentSnapshot.Source.AdapterKey,
                region = currentItem.Region.Key,
                metric = currentItem.Metric.Key,
                signal,
                transitionKind,
                previousStatusKey,
                previousStatusLabel = ReserveStatusCatalog.GetLabel(previousStatusKey),
                currentStatusKey,
                currentStatusLabel = ReserveStatusCatalog.GetLabel(currentStatusKey),
                capturedAtUtc = currentSnapshot.CapturedAtUtc,
                referenceDate = currentSnapshot.ReferenceDate,
            });

            events.Add(new Event(
                RuleKey,
                currentSnapshot.Source,
                currentItem.Metric,
                currentItem.Region,
                DateTime.UtcNow,
                payloadJson));
        }

        return Task.FromResult<IReadOnlyCollection<Event>>(events);
    }

    private static string BuildKey(string regionKey, string metricKey)
    {
        return $"{regionKey}|{metricKey}";
    }

    private static string? ResolveSignal(string previousStatusKey, string currentStatusKey)
    {
        var previousIsNormal = ReserveStatusCatalog.IsNormal(previousStatusKey);
        var currentIsNormal = ReserveStatusCatalog.IsNormal(currentStatusKey);

        if (previousIsNormal && !currentIsNormal)
        {
            return "status-alert";
        }

        if (!previousIsNormal && !currentIsNormal)
        {
            var previousRank = ReserveStatusCatalog.GetRank(previousStatusKey);
            var currentRank = ReserveStatusCatalog.GetRank(currentStatusKey);
            if (currentRank > previousRank)
            {
                return "status-alert";
            }
        }

        if (!previousIsNormal && currentIsNormal)
        {
            return "recovery";
        }

        return null;
    }

    private static string ResolveTransitionKind(string previousStatusKey, string currentStatusKey)
    {
        var previousIsNormal = ReserveStatusCatalog.IsNormal(previousStatusKey);
        var currentIsNormal = ReserveStatusCatalog.IsNormal(currentStatusKey);

        if (previousIsNormal && !currentIsNormal)
        {
            return "entered-non-normal";
        }

        if (!previousIsNormal && !currentIsNormal)
        {
            return "worsened";
        }

        if (!previousIsNormal && currentIsNormal)
        {
            return "recovered-to-normal";
        }

        return "state-unchanged";
    }
}
