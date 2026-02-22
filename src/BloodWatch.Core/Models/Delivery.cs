namespace BloodWatch.Core.Models;

public sealed record Delivery(
    string TypeKey,
    string Target,
    DeliveryStatus Status,
    DateTime CreatedAtUtc,
    string? LastError = null,
    DateTime? SentAtUtc = null,
    DeliveryFailureKind FailureKind = DeliveryFailureKind.None);
