namespace BloodWatch.Infrastructure.Persistence.Entities;

public sealed class DeliveryEntity
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public Guid SubscriptionId { get; set; }
    public int AttemptCount { get; set; }
    public string Status { get; set; } = "pending";
    public string? LastError { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }

    public EventEntity Event { get; set; } = null!;
    public SubscriptionEntity Subscription { get; set; } = null!;
}
