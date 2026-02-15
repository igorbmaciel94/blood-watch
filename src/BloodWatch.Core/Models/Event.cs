namespace BloodWatch.Core.Models;

public sealed record Event(
    string RuleKey,
    SourceRef Source,
    Metric Metric,
    RegionRef? Region,
    DateTime CreatedAtUtc,
    string PayloadJson);
