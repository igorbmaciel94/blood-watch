namespace BloodWatch.Core.Models;

public sealed record SnapshotItem(
    Metric Metric,
    RegionRef Region,
    string StatusKey,
    string StatusLabel,
    decimal? Value = null,
    string? Unit = null);
