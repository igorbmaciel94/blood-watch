namespace BloodWatch.Infrastructure.Persistence.Entities;

public sealed class EventEntity
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public Guid SnapshotId { get; set; }
    public Guid? RegionId { get; set; }
    public string RuleKey { get; set; } = string.Empty;
    public string MetricKey { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAtUtc { get; set; }

    public SourceEntity Source { get; set; } = null!;
    public SnapshotEntity Snapshot { get; set; } = null!;
    public RegionEntity? Region { get; set; }
    public ICollection<DeliveryEntity> Deliveries { get; set; } = [];
}
