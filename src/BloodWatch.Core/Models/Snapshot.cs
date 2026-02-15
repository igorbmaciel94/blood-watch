namespace BloodWatch.Core.Models;

public sealed record Snapshot(
    SourceRef Source,
    DateTime CapturedAtUtc,
    DateOnly? ReferenceDate,
    IReadOnlyCollection<SnapshotItem> Items);
