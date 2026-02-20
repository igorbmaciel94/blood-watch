namespace BloodWatch.Infrastructure.Persistence.Entities;

public sealed class SubscriptionEntity
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public string TypeKey { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string ScopeType { get; set; } = string.Empty;
    public string? RegionFilter { get; set; }
    public Guid? InstitutionId { get; set; }
    public string MetricFilter { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? DisabledAtUtc { get; set; }

    public SourceEntity Source { get; set; } = null!;
    public DonationCenterEntity? Institution { get; set; }
    public ICollection<DeliveryEntity> Deliveries { get; set; } = [];
}
