using System.Text.RegularExpressions;
using BloodWatch.Api.Services;
using BloodWatch.Core.Models;
using BloodWatch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BloodWatch.Api.Copilot;

public sealed partial class CopilotAnalyticsTools(
    BloodWatchDbContext dbContext,
    IReserveAnalyticsQueryService reserveAnalyticsQueryService)
{
    private readonly BloodWatchDbContext _dbContext = dbContext;
    private readonly IReserveAnalyticsQueryService _reserveAnalyticsQueryService = reserveAnalyticsQueryService;

    public async Task<CopilotSourceContext?> ResolveSourceAsync(string sourceKey, CancellationToken cancellationToken)
    {
        var normalizedSourceKey = Normalize(sourceKey);
        if (normalizedSourceKey is null)
        {
            return null;
        }

        return await _dbContext.Sources
            .AsNoTracking()
            .Where(source => source.AdapterKey == normalizedSourceKey)
            .Select(source => new CopilotSourceContext(source.Id, source.AdapterKey))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<CopilotToolOutput> GetCurrentCriticalAsync(
        Guid sourceId,
        string sourceKey,
        CancellationToken cancellationToken)
    {
        var rows = await (
                from reserve in _dbContext.CurrentReserves.AsNoTracking()
                join region in _dbContext.Regions.AsNoTracking() on reserve.RegionId equals region.Id
                where reserve.SourceId == sourceId && reserve.StatusKey == ReserveStatusCatalog.Critical
                orderby region.Key, reserve.MetricKey
                select new
                {
                    region.Key,
                    region.DisplayName,
                    reserve.MetricKey,
                    reserve.StatusKey,
                    reserve.StatusLabel,
                    reserve.ReferenceDate,
                    reserve.CapturedAtUtc,
                })
            .ToListAsync(cancellationToken);

        var entries = rows
            .Select(row => new CopilotToolEntry(
                ResultId: $"critical:{sourceKey}:{row.Key}:{row.MetricKey}",
                Data: new Dictionary<string, object?>
                {
                    ["source"] = sourceKey,
                    ["regionKey"] = row.Key,
                    ["regionName"] = row.DisplayName,
                    ["metric"] = row.MetricKey,
                    ["statusKey"] = row.StatusKey,
                    ["statusLabel"] = row.StatusLabel,
                    ["referenceDate"] = row.ReferenceDate?.ToString("yyyy-MM-dd"),
                    ["capturedAtUtc"] = row.CapturedAtUtc,
                }))
            .ToArray();

        return new CopilotToolOutput(
            CopilotConstants.CurrentCriticalQueryId,
            "Current critical reserve statuses by region and metric.",
            entries);
    }

    public async Task<CopilotToolOutput> GetWeeklyDeltaAsync(
        Guid sourceId,
        string sourceKey,
        int limit,
        CancellationToken cancellationToken)
    {
        var response = await _reserveAnalyticsQueryService.GetReserveDeltasAsync(
            sourceId,
            sourceKey,
            Math.Clamp(limit, 1, 1000),
            cancellationToken);

        if (response is null)
        {
            return new CopilotToolOutput(
                CopilotConstants.WeeklyDeltaQueryId,
                "Reserve status changes since previous reference date.",
                Array.Empty<CopilotToolEntry>());
        }

        var entries = response.Items
            .Select(item => new CopilotToolEntry(
                ResultId: $"delta:{sourceKey}:{item.Region.Key}:{item.Metric}:{item.PreviousStatusKey}->{item.CurrentStatusKey}",
                Data: new Dictionary<string, object?>
                {
                    ["source"] = sourceKey,
                    ["regionKey"] = item.Region.Key,
                    ["regionName"] = item.Region.Name,
                    ["metric"] = item.Metric,
                    ["previousStatusKey"] = item.PreviousStatusKey,
                    ["currentStatusKey"] = item.CurrentStatusKey,
                    ["rankDelta"] = item.RankDelta,
                    ["previousReferenceDate"] = response.PreviousReferenceDate.ToString("yyyy-MM-dd"),
                    ["currentReferenceDate"] = response.CurrentReferenceDate.ToString("yyyy-MM-dd"),
                }))
            .ToArray();

        return new CopilotToolOutput(
            CopilotConstants.WeeklyDeltaQueryId,
            "Reserve status changes since previous reference date.",
            entries);
    }

    public async Task<CopilotToolOutput> GetTopDowngradesAsync(
        Guid sourceId,
        string sourceKey,
        int weeks,
        int limit,
        CancellationToken cancellationToken)
    {
        var response = await _reserveAnalyticsQueryService.GetTopDowngradesAsync(
            sourceId,
            sourceKey,
            Math.Clamp(weeks, 1, 104),
            Math.Clamp(limit, 1, 200),
            cancellationToken);

        if (response is null)
        {
            return new CopilotToolOutput(
                CopilotConstants.TopDowngradesQueryId,
                "Top regions with most downgrades in the selected window.",
                Array.Empty<CopilotToolEntry>());
        }

        var entries = response.Items
            .Select(item => new CopilotToolEntry(
                ResultId: $"downgrade:{sourceKey}:{item.Region.Key}",
                Data: new Dictionary<string, object?>
                {
                    ["source"] = sourceKey,
                    ["regionKey"] = item.Region.Key,
                    ["regionName"] = item.Region.Name,
                    ["downgrades"] = item.Downgrades,
                    ["fromReferenceDate"] = response.FromReferenceDate.ToString("yyyy-MM-dd"),
                    ["toReferenceDate"] = response.ToReferenceDate.ToString("yyyy-MM-dd"),
                    ["weeks"] = response.Weeks,
                }))
            .ToArray();

        return new CopilotToolOutput(
            CopilotConstants.TopDowngradesQueryId,
            "Top regions with most downgrades in the selected window.",
            entries);
    }

    public async Task<CopilotToolOutput> GetUnstableMetricsAsync(
        Guid sourceId,
        string sourceKey,
        int weeks,
        int limit,
        CancellationToken cancellationToken)
    {
        var response = await _reserveAnalyticsQueryService.GetUnstableMetricsAsync(
            sourceId,
            sourceKey,
            Math.Clamp(weeks, 1, 104),
            Math.Clamp(limit, 1, 200),
            cancellationToken);

        if (response is null)
        {
            return new CopilotToolOutput(
                CopilotConstants.UnstableMetricsQueryId,
                "Most unstable metrics by region in the selected window.",
                Array.Empty<CopilotToolEntry>());
        }

        var entries = response.Items
            .Select(item => new CopilotToolEntry(
                ResultId: $"unstable:{sourceKey}:{item.Region.Key}:{item.Metric}",
                Data: new Dictionary<string, object?>
                {
                    ["source"] = sourceKey,
                    ["regionKey"] = item.Region.Key,
                    ["regionName"] = item.Region.Name,
                    ["metric"] = item.Metric,
                    ["transitions"] = item.Transitions,
                    ["fromReferenceDate"] = response.FromReferenceDate.ToString("yyyy-MM-dd"),
                    ["toReferenceDate"] = response.ToReferenceDate.ToString("yyyy-MM-dd"),
                    ["weeks"] = response.Weeks,
                }))
            .ToArray();

        return new CopilotToolOutput(
            CopilotConstants.UnstableMetricsQueryId,
            "Most unstable metrics by region in the selected window.",
            entries);
    }

    public async Task<CopilotToolOutput> GetFailedDeliveriesAsync(
        Guid sourceId,
        string sourceKey,
        DateTime windowStartUtc,
        DateTime windowEndUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        var rows = await (
                from delivery in _dbContext.Deliveries.AsNoTracking()
                join subscription in _dbContext.Subscriptions.AsNoTracking() on delivery.SubscriptionId equals subscription.Id
                where subscription.SourceId == sourceId
                      && delivery.CreatedAtUtc >= windowStartUtc
                      && delivery.CreatedAtUtc <= windowEndUtc
                      && delivery.Status != "sent"
                orderby delivery.CreatedAtUtc descending
                select new
                {
                    delivery.Id,
                    delivery.EventId,
                    delivery.Status,
                    delivery.AttemptCount,
                    delivery.LastError,
                    delivery.CreatedAtUtc,
                    subscription.TypeKey,
                })
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken);

        var entries = rows
            .Select(row => new CopilotToolEntry(
                ResultId: $"fail:{row.Id}",
                Data: new Dictionary<string, object?>
                {
                    ["source"] = sourceKey,
                    ["deliveryId"] = row.Id,
                    ["eventId"] = row.EventId,
                    ["status"] = row.Status,
                    ["attemptCount"] = row.AttemptCount,
                    ["subscriptionType"] = NormalizeSubscriptionType(row.TypeKey),
                    ["reason"] = SanitizeErrorReason(row.LastError),
                    ["createdAtUtc"] = row.CreatedAtUtc,
                }))
            .ToArray();

        return new CopilotToolOutput(
            CopilotConstants.FailedDeliveriesQueryId,
            "Recent failed deliveries with sanitized reasons.",
            entries);
    }

    public async Task<CopilotToolOutput> GetFailingSubscriptionTypesAsync(
        Guid sourceId,
        string sourceKey,
        DateTime windowStartUtc,
        DateTime windowEndUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        var rows = await (
                from delivery in _dbContext.Deliveries.AsNoTracking()
                join subscription in _dbContext.Subscriptions.AsNoTracking() on delivery.SubscriptionId equals subscription.Id
                where subscription.SourceId == sourceId
                      && delivery.CreatedAtUtc >= windowStartUtc
                      && delivery.CreatedAtUtc <= windowEndUtc
                      && delivery.Status != "sent"
                group delivery by subscription.TypeKey
                into grouped
                orderby grouped.Count() descending
                select new
                {
                    TypeKey = grouped.Key,
                    FailedCount = grouped.Count(),
                })
            .Take(Math.Clamp(limit, 1, 50))
            .ToListAsync(cancellationToken);

        var entries = rows
            .Select(row =>
            {
                var normalizedType = NormalizeSubscriptionType(row.TypeKey);
                return new CopilotToolEntry(
                    ResultId: $"failtype:{normalizedType}:{windowStartUtc:O}:{windowEndUtc:O}",
                    Data: new Dictionary<string, object?>
                    {
                        ["source"] = sourceKey,
                        ["typeKey"] = normalizedType,
                        ["failedCount"] = row.FailedCount,
                        ["windowStartUtc"] = windowStartUtc,
                        ["windowEndUtc"] = windowEndUtc,
                    });
            })
            .ToArray();

        return new CopilotToolOutput(
            CopilotConstants.FailingSubscriptionTypesQueryId,
            "Subscription channel types with highest failure counts (sanitized).",
            entries);
    }

    private static string NormalizeSubscriptionType(string rawType)
    {
        return NotificationChannelTypeCatalog.TryNormalizeStored(rawType, out var normalized)
            ? normalized
            : rawType;
    }

    public static string SanitizeErrorReason(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return "Unknown failure";
        }

        const int maxLength = 300;

        var sanitized = rawValue.Trim();
        sanitized = UrlRegex().Replace(sanitized, "[redacted-url]");
        sanitized = KeyValueSecretRegex().Replace(sanitized, "$1=[redacted]");
        sanitized = LongNumberRegex().Replace(sanitized, "***");

        if (sanitized.Length > maxLength)
        {
            sanitized = sanitized[..maxLength];
        }

        return sanitized;
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    [GeneratedRegex(@"https?://\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"(?i)(token|secret|password|credential|api[-_]?key|webhook)\s*[=:]\s*\S+", RegexOptions.CultureInvariant)]
    private static partial Regex KeyValueSecretRegex();

    [GeneratedRegex(@"\b\d{6,}\b", RegexOptions.CultureInvariant)]
    private static partial Regex LongNumberRegex();
}
