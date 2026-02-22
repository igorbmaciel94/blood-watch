namespace BloodWatch.Api.Contracts;

public sealed record CreateTokenRequest(string? Email, string? Password);

public sealed record CreateTokenResponse(
    string AccessToken,
    string TokenType,
    DateTime ExpiresAtUtc);

public sealed record CreateSubscriptionRequest(
    string? Source,
    string? Type,
    string? Target,
    string? ScopeType,
    string? Region,
    Guid? InstitutionId,
    string? Metric);

public sealed record GetSubscriptionsQuery(
    string? Source,
    string? ScopeType,
    string? Region,
    Guid? InstitutionId,
    string? Metric);

public sealed record SubscriptionDeliveriesQuery(int? Limit);

public sealed record SubscriptionsResponse(IReadOnlyCollection<SubscriptionResponse> Items);

public sealed record SubscriptionResponse(
    Guid Id,
    string Source,
    string Type,
    string ScopeType,
    string? Region,
    Guid? InstitutionId,
    string? Metric,
    string Target,
    bool IsEnabled,
    DateTime CreatedAtUtc,
    DateTime? DisabledAtUtc);

public sealed record SubscriptionDeliveriesResponse(
    Guid SubscriptionId,
    IReadOnlyCollection<SubscriptionDeliveryResponse> Items);

public sealed record SubscriptionDeliveryResponse(
    Guid EventId,
    string Status,
    int AttemptCount,
    string? LastError,
    DateTime CreatedAtUtc,
    DateTime? SentAtUtc);
