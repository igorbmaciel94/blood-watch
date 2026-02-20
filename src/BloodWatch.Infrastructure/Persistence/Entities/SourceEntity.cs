namespace BloodWatch.Infrastructure.Persistence.Entities;

public sealed class SourceEntity
{
    public Guid Id { get; set; }
    public string AdapterKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastPolledAtUtc { get; set; }

    public ICollection<RegionEntity> Regions { get; set; } = [];
    public ICollection<CurrentReserveEntity> CurrentReserves { get; set; } = [];
    public ICollection<DonationCenterEntity> DonationCenters { get; set; } = [];
    public ICollection<CollectionSessionEntity> CollectionSessions { get; set; } = [];
    public ICollection<SubscriptionEntity> Subscriptions { get; set; } = [];
    public ICollection<EventEntity> Events { get; set; } = [];
}
