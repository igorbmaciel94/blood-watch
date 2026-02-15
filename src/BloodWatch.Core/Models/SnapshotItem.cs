namespace BloodWatch.Core.Models;

public sealed record SnapshotItem(
    Metric Metric,
    RegionRef Region,
    decimal Value,
    string Unit,
    string? Severity = null);
