namespace BloodWatch.Api.Contracts;

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
