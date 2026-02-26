using BloodWatch.Api.Contracts;

namespace BloodWatch.Api.Services;

public interface IReserveAnalyticsQueryService
{
    Task<ReserveDeltasResponse?> GetReserveDeltasAsync(
        Guid sourceId,
        string sourceKey,
        int limit,
        CancellationToken cancellationToken);

    Task<TopDowngradesResponse?> GetTopDowngradesAsync(
        Guid sourceId,
        string sourceKey,
        int weeks,
        int limit,
        CancellationToken cancellationToken);

    Task<TimeInStatusResponse?> GetTimeInStatusAsync(
        Guid sourceId,
        string sourceKey,
        int weeks,
        int limit,
        CancellationToken cancellationToken);

    Task<UnstableMetricsResponse?> GetUnstableMetricsAsync(
        Guid sourceId,
        string sourceKey,
        int weeks,
        int limit,
        CancellationToken cancellationToken);
}
