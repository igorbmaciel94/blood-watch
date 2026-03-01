namespace BloodWatch.Api.Copilot;

public sealed class CopilotIntentRouter
{
    public IReadOnlyCollection<string> SelectQueryIds(string question)
    {
        var normalized = question.Trim().ToLowerInvariant();
        var queryIds = new HashSet<string>(StringComparer.Ordinal)
        {
            CopilotConstants.CurrentCriticalQueryId,
        };

        if (ContainsAny(normalized, "change", "changed", "delta", "since last", "last week"))
        {
            queryIds.Add(CopilotConstants.WeeklyDeltaQueryId);
        }

        if (ContainsAny(normalized, "downgrade", "unstable", "instability", "volatile", "transitions"))
        {
            queryIds.Add(CopilotConstants.TopDowngradesQueryId);
            queryIds.Add(CopilotConstants.UnstableMetricsQueryId);
        }

        if (ContainsAny(normalized, "delivery", "deliveries", "notification", "notifications", "failed", "fail", "subscription types"))
        {
            queryIds.Add(CopilotConstants.FailedDeliveriesQueryId);
            queryIds.Add(CopilotConstants.FailingSubscriptionTypesQueryId);
        }

        if (queryIds.Count == 1)
        {
            queryIds.Add(CopilotConstants.WeeklyDeltaQueryId);
            queryIds.Add(CopilotConstants.UnstableMetricsQueryId);
        }

        return queryIds.ToArray();
    }

    private static bool ContainsAny(string value, params string[] terms)
    {
        return terms.Any(term => value.Contains(term, StringComparison.Ordinal));
    }
}
