using Microsoft.Extensions.Options;

namespace BloodWatch.Worker.Alerts;

public sealed class AlertThresholdProfileResolver(
    IOptions<AlertThresholdOptions> options,
    CompatibilityPriorityService compatibilityPriorityService)
{
    private readonly IOptions<AlertThresholdOptions> _options = options;
    private readonly CompatibilityPriorityService _compatibilityPriorityService = compatibilityPriorityService;

    public AlertThresholdProfile Resolve(string metricKey)
    {
        var normalizedMetricKey = string.IsNullOrWhiteSpace(metricKey)
            ? "overall"
            : metricKey.Trim();

        var settings = _options.Value;
        var baseCriticalUnits = ClampPositive(settings.BaseCriticalUnits, 100m);
        var warningMultiplier = Clamp(settings.WarningMultiplier, 1.01m, 10m);
        var stepDownPercent = Clamp(settings.CriticalStepDownPercent, 0.01m, 1m);

        var hasOverride = settings.MetricCriticalUnitsOverrides.TryGetValue(normalizedMetricKey, out var explicitCriticalUnits)
            && explicitCriticalUnits > 0m;

        var priorityWeight = _compatibilityPriorityService.GetPriorityWeight(normalizedMetricKey);
        var criticalUnits = hasOverride
            ? explicitCriticalUnits
            : baseCriticalUnits * priorityWeight;

        criticalUnits = ClampPositive(criticalUnits, baseCriticalUnits);

        var warningUnits = criticalUnits * warningMultiplier;
        var stepDownUnits = Math.Max(criticalUnits * stepDownPercent, 1m);

        return new AlertThresholdProfile(
            CriticalUnits: criticalUnits,
            WarningUnits: warningUnits,
            StepDownUnits: stepDownUnits,
            PriorityWeight: priorityWeight,
            HasExplicitOverride: hasOverride);
    }

    private static decimal Clamp(decimal value, decimal min, decimal max)
    {
        return Math.Min(max, Math.Max(min, value));
    }

    private static decimal ClampPositive(decimal value, decimal fallback)
    {
        return value > 0m ? value : fallback;
    }
}
