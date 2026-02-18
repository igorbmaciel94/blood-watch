namespace BloodWatch.Infrastructure.Persistence.Entities;

public sealed class RegionEntity
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }

    public SourceEntity Source { get; set; } = null!;
    public ICollection<CurrentReserveEntity> CurrentReserves { get; set; } = [];
    public ICollection<EventEntity> Events { get; set; } = [];
}
