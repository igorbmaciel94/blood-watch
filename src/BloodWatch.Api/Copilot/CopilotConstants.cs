namespace BloodWatch.Api.Copilot;

public static class CopilotConstants
{
    public const string Disclaimer = "No medical advice. BloodWatch reports public data and operational analytics only.";

    public const string CurrentCriticalQueryId = "bw.current-critical.v1";
    public const string WeeklyDeltaQueryId = "bw.weekly-delta.v1";
    public const string TopDowngradesQueryId = "bw.top-downgrades.v1";
    public const string UnstableMetricsQueryId = "bw.unstable-metrics.v1";
    public const string FailedDeliveriesQueryId = "bw.failed-deliveries.v1";
    public const string FailingSubscriptionTypesQueryId = "bw.failing-subscription-types.v1";

    public static readonly string[] AllQueryIds =
    [
        CurrentCriticalQueryId,
        WeeklyDeltaQueryId,
        TopDowngradesQueryId,
        UnstableMetricsQueryId,
        FailedDeliveriesQueryId,
        FailingSubscriptionTypesQueryId,
    ];
}
