namespace BloodWatch.Worker.Alerts;

public sealed class AlertThresholdOptions
{
    public const string SectionName = "BloodWatch:Alerts";

    public decimal BaseCriticalUnits { get; set; } = 100m;
    public decimal WarningMultiplier { get; set; } = 1.2m;
    public decimal CriticalStepDownPercent { get; set; } = 0.10m;
    public int ReminderIntervalHours { get; set; } = 24;
    public int WorseningBucketDelta { get; set; } = 1;
    public bool SendRecoveryNotification { get; set; } = true;
    public Dictionary<string, decimal> MetricCriticalUnitsOverrides { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed record AlertThresholdProfile(
    decimal CriticalUnits,
    decimal WarningUnits,
    decimal StepDownUnits,
    decimal PriorityWeight,
    bool HasExplicitOverride);
