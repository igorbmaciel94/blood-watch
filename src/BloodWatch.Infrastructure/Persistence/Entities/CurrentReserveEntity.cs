namespace BloodWatch.Infrastructure.Persistence.Entities;

public sealed class CurrentReserveEntity
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public Guid RegionId { get; set; }
    public string MetricKey { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string? Severity { get; set; }
    public DateOnly? ReferenceDate { get; set; }
    public DateTime CapturedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public SourceEntity Source { get; set; } = null!;
    public RegionEntity Region { get; set; } = null!;
    public ICollection<EventEntity> Events { get; set; } = [];
}
