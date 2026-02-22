using BloodWatch.Api.Contracts;

namespace BloodWatch.Api.Services;

public interface ISubscriptionService
{
    Task<ServiceResult<SubscriptionsResponse>> GetSubscriptionsAsync(GetSubscriptionsQuery query, CancellationToken cancellationToken);

    Task<ServiceResult<SubscriptionResponse>> CreateSubscriptionAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken);

    Task<ServiceResult<SubscriptionResponse>> GetSubscriptionByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<ServiceResult<SubscriptionDeliveriesResponse>> GetSubscriptionDeliveriesAsync(
        Guid id,
        int? limit,
        CancellationToken cancellationToken);

    Task<ServiceResult> DisableSubscriptionAsync(Guid id, CancellationToken cancellationToken);
}
