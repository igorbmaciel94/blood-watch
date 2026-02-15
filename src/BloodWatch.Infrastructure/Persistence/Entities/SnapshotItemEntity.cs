namespace BloodWatch.Infrastructure.Persistence.Entities;

public sealed class SnapshotItemEntity
{
    public Guid Id { get; set; }
    public Guid SnapshotId { get; set; }
    public Guid RegionId { get; set; }
    public string MetricKey { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string? Severity { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public SnapshotEntity Snapshot { get; set; } = null!;
    public RegionEntity Region { get; set; } = null!;
}
