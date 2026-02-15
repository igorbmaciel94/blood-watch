namespace BloodWatch.Infrastructure.Persistence.Entities;

public sealed class SnapshotEntity
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public DateTime CapturedAtUtc { get; set; }
    public DateOnly? ReferenceDate { get; set; }
    public string Hash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }

    public SourceEntity Source { get; set; } = null!;
    public ICollection<SnapshotItemEntity> Items { get; set; } = [];
    public ICollection<EventEntity> Events { get; set; } = [];
}
