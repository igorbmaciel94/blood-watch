namespace BloodWatch.Api.Contracts;

public sealed record CreateSubscriptionRequest(
    string? Source,
    string? Type,
    string? Target,
    string? Region,
    string? Metric);

public sealed record GetSubscriptionsQuery(
    string? Source,
    string? Region,
    string? Metric);

public sealed record SubscriptionsResponse(IReadOnlyCollection<SubscriptionResponse> Items);

public sealed record SubscriptionResponse(
    Guid Id,
    string Source,
    string Type,
    string Target,
    string Region,
    string Metric,
    bool IsEnabled,
    DateTime CreatedAtUtc,
    DateTime? DisabledAtUtc);
