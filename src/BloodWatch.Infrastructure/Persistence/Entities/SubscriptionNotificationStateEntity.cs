namespace BloodWatch.Infrastructure.Persistence.Entities;

public sealed class SubscriptionNotificationStateEntity
{
    public Guid SubscriptionId { get; set; }
    public bool IsLowOpen { get; set; }
    public DateTime? LastLowNotifiedAtUtc { get; set; }
    public int? LastLowNotifiedBucket { get; set; }
    public decimal? LastLowNotifiedUnits { get; set; }
    public DateTime? LastRecoveryNotifiedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public SubscriptionEntity Subscription { get; set; } = null!;
}
