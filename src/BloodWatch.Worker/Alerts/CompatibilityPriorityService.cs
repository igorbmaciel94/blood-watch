namespace BloodWatch.Worker.Alerts;

public sealed class CompatibilityPriorityService
{
    private static readonly IReadOnlyDictionary<string, decimal> MetricWeights = new Dictionary<string, decimal>(StringComparer.Ordinal)
    {
        ["blood-group-o-minus"] = 1.4m,
        ["blood-group-o-plus"] = 1.2m,
        ["blood-group-a-minus"] = 1.1m,
        ["blood-group-b-minus"] = 1.1m,
        ["blood-group-a-plus"] = 1.0m,
        ["blood-group-b-plus"] = 1.0m,
        ["blood-group-ab-minus"] = 0.9m,
        ["blood-group-ab-plus"] = 0.8m,
        ["overall"] = 1.0m,
    };

    public decimal GetPriorityWeight(string metricKey)
    {
        if (string.IsNullOrWhiteSpace(metricKey))
        {
            return 1.0m;
        }

        return MetricWeights.TryGetValue(metricKey.Trim(), out var weight)
            ? weight
            : 1.0m;
    }
}
