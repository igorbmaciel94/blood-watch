namespace BloodWatch.Infrastructure.Persistence.Entities;

public sealed class ReserveHistoryObservationEntity
{
    public Guid SourceId { get; set; }
    public Guid RegionId { get; set; }
    public string MetricKey { get; set; } = string.Empty;
    public string StatusKey { get; set; } = string.Empty;
    public short StatusRank { get; set; }
    public DateOnly ReferenceDate { get; set; }
    public DateTime CapturedAtUtc { get; set; }

    public SourceEntity Source { get; set; } = null!;
    public RegionEntity Region { get; set; } = null!;
}
