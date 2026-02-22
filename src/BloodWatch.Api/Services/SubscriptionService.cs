using System.Text.RegularExpressions;
using BloodWatch.Api.Contracts;
using BloodWatch.Core.Models;
using BloodWatch.Infrastructure.Persistence;
using BloodWatch.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BloodWatch.Api.Services;

public sealed class SubscriptionService(BloodWatchDbContext dbContext) : ISubscriptionService
{
    private const int DefaultDeliveriesLimit = 20;
    private const int MaxDeliveriesLimit = 100;
    private const string ScopeTypeRegion = "region";
    private const string ScopeTypeInstitution = "institution";
    private const string WildcardMetricToken = "*";

    private static readonly Regex MetricKeyRegex = new(
        "^[a-z0-9]+(?:-[a-z0-9]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TelegramChatIdRegex = new(
        "^-?[0-9]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex VersionedWebhookPathRegex = new(
        "^/api/v\\d+/webhooks/",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public async Task<ServiceResult<SubscriptionsResponse>> GetSubscriptionsAsync(
        GetSubscriptionsQuery query,
        CancellationToken cancellationToken)
    {
        var sourceKey = NormalizeRequired(query.Source);
        var scopeType = NormalizeRequired(query.ScopeType);
        var regionKey = NormalizeRequired(query.Region);
        var metricKey = NormalizeRequired(query.Metric);

        var subscriptionsQuery = dbContext.Subscriptions
            .AsNoTracking()
            .Where(entry => entry.IsEnabled)
            .AsQueryable();

        if (sourceKey is not null)
        {
            subscriptionsQuery = subscriptionsQuery.Where(entry => entry.Source.AdapterKey == sourceKey);
        }

        if (scopeType is not null)
        {
            subscriptionsQuery = subscriptionsQuery.Where(entry => entry.ScopeType == scopeType);
        }

        if (regionKey is not null)
        {
            subscriptionsQuery = subscriptionsQuery.Where(entry => entry.RegionFilter == regionKey);
        }

        if (query.InstitutionId.HasValue)
        {
            subscriptionsQuery = subscriptionsQuery.Where(entry => entry.InstitutionId == query.InstitutionId.Value);
        }

        if (metricKey is not null)
        {
            subscriptionsQuery = subscriptionsQuery.Where(entry => entry.MetricFilter == metricKey);
        }

        var rows = await subscriptionsQuery
            .OrderByDescending(entry => entry.CreatedAtUtc)
            .ThenBy(entry => entry.Id)
            .Select(entry => new SubscriptionProjection(
                entry.Id,
                entry.Source.AdapterKey,
                entry.TypeKey,
                entry.ScopeType,
                entry.RegionFilter,
                entry.InstitutionId,
                ToApiMetric(entry.MetricFilter),
                entry.Target,
                entry.IsEnabled,
                entry.CreatedAtUtc,
                entry.DisabledAtUtc))
            .ToArrayAsync(cancellationToken);

        var items = rows
            .Select(MapSubscriptionResponse)
            .ToArray();

        return ServiceResult<SubscriptionsResponse>.Success(new SubscriptionsResponse(items));
    }

    public async Task<ServiceResult<SubscriptionResponse>> CreateSubscriptionAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        var sourceKey = NormalizeRequired(request.Source);
        if (sourceKey is null)
        {
            return ServiceResult<SubscriptionResponse>.Failure(
                StatusCodes.Status400BadRequest,
                "Bad request",
                "Field 'source' is required.");
        }

        var typeKey = NormalizeRequired(request.Type);
        if (typeKey is null)
        {
            return ServiceResult<SubscriptionResponse>.Failure(
                StatusCodes.Status400BadRequest,
                "Bad request",
                "Field 'type' is required.");
        }

        if (string.Equals(typeKey, NotificationChannelTypeCatalog.LegacyDiscordWebhook, StringComparison.Ordinal)
            || string.Equals(typeKey, NotificationChannelTypeCatalog.LegacyTelegramChat, StringComparison.Ordinal)
            || !NotificationChannelTypeCatalog.IsCanonical(typeKey))
        {
            return ServiceResult<SubscriptionResponse>.Failure(
                StatusCodes.Status400BadRequest,
                "Bad request",
                $"Field 'type' must be '{NotificationChannelTypeCatalog.DiscordWebhook}' or '{NotificationChannelTypeCatalog.TelegramChat}'.");
        }

        var scopeType = NormalizeRequired(request.ScopeType);
        if (scopeType is null)
        {
            return ServiceResult<SubscriptionResponse>.Failure(
                StatusCodes.Status400BadRequest,
                "Bad request",
                "Field 'scopeType' is required.");
        }

        if (!string.Equals(scopeType, ScopeTypeRegion, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(scopeType, ScopeTypeInstitution, StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<SubscriptionResponse>.Failure(
                StatusCodes.Status400BadRequest,
                "Bad request",
                $"Field 'scopeType' must be '{ScopeTypeRegion}' or '{ScopeTypeInstitution}'.");
        }

        scopeType = scopeType.ToLowerInvariant();

        var metricKey = NormalizeRequired(request.Metric);
        if (metricKey is not null && !MetricKeyRegex.IsMatch(metricKey))
        {
            return ServiceResult<SubscriptionResponse>.Failure(
                StatusCodes.Status400BadRequest,
                "Bad request",
                "Field 'metric' has an invalid format.");
        }

        var target = NormalizeRequired(request.Target);
        if (target is null)
        {
            return ServiceResult<SubscriptionResponse>.Failure(
                StatusCodes.Status400BadRequest,
                "Bad request",
                "Field 'target' is required.");
        }

        if (!IsValidTarget(typeKey, target))
        {
            return string.Equals(typeKey, NotificationChannelTypeCatalog.DiscordWebhook, StringComparison.Ordinal)
                ? ServiceResult<SubscriptionResponse>.Failure(
                    StatusCodes.Status400BadRequest,
                    "Bad request",
                    "Field 'target' must be a valid Discord webhook URL.")
                : ServiceResult<SubscriptionResponse>.Failure(
                    StatusCodes.Status400BadRequest,
                    "Bad request",
                    "Field 'target' must be a valid Telegram chat id (numeric string).");
        }

        var source = await dbContext.Sources
            .SingleOrDefaultAsync(entry => entry.AdapterKey == sourceKey, cancellationToken);

        if (source is null)
        {
            return ServiceResult<SubscriptionResponse>.Failure(
                StatusCodes.Status404NotFound,
                "Not found",
                $"Source '{sourceKey}' was not found.");
        }

        string? regionKey = null;
        Guid? institutionId = null;

        if (scopeType == ScopeTypeRegion)
        {
            regionKey = NormalizeRequired(request.Region);
            if (regionKey is null)
            {
                return ServiceResult<SubscriptionResponse>.Failure(
                    StatusCodes.Status400BadRequest,
                    "Bad request",
                    "Field 'region' is required when scopeType is 'region'.");
            }

            if (request.InstitutionId.HasValue)
            {
                return ServiceResult<SubscriptionResponse>.Failure(
                    StatusCodes.Status400BadRequest,
                    "Bad request",
                    "Field 'institutionId' must be null when scopeType is 'region'.");
            }

            var regionExists = await dbContext.Regions
                .AnyAsync(region => region.SourceId == source.Id && region.Key == regionKey, cancellationToken);

            if (!regionExists)
            {
                return ServiceResult<SubscriptionResponse>.Failure(
                    StatusCodes.Status404NotFound,
                    "Not found",
                    $"Region '{regionKey}' was not found for source '{source.AdapterKey}'.");
            }
        }
        else
        {
            if (!request.InstitutionId.HasValue)
            {
                return ServiceResult<SubscriptionResponse>.Failure(
                    StatusCodes.Status400BadRequest,
                    "Bad request",
                    "Field 'institutionId' is required when scopeType is 'institution'.");
            }

            if (NormalizeRequired(request.Region) is not null)
            {
                return ServiceResult<SubscriptionResponse>.Failure(
                    StatusCodes.Status400BadRequest,
                    "Bad request",
                    "Field 'region' must be null when scopeType is 'institution'.");
            }

            institutionId = request.InstitutionId.Value;

            var institutionExists = await dbContext.DonationCenters
                .AnyAsync(center => center.SourceId == source.Id && center.Id == institutionId.Value, cancellationToken);

            if (!institutionExists)
            {
                return ServiceResult<SubscriptionResponse>.Failure(
                    StatusCodes.Status404NotFound,
                    "Not found",
                    $"Institution '{institutionId}' was not found for source '{source.AdapterKey}'.");
            }
        }

        var hasAnyMetrics = await dbContext.CurrentReserves
            .AnyAsync(reserve => reserve.SourceId == source.Id, cancellationToken);

        if (hasAnyMetrics && metricKey is not null)
        {
            var metricExists = await dbContext.CurrentReserves
                .AnyAsync(reserve => reserve.SourceId == source.Id && reserve.MetricKey == metricKey, cancellationToken);

            if (!metricExists)
            {
                return ServiceResult<SubscriptionResponse>.Failure(
                    StatusCodes.Status400BadRequest,
                    "Bad request",
                    $"Metric '{metricKey}' was not found for source '{source.AdapterKey}'.");
            }
        }

        var storedMetricFilter = metricKey ?? WildcardMetricToken;

        var entity = new SubscriptionEntity
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            TypeKey = typeKey,
            Target = target,
            ScopeType = scopeType,
            RegionFilter = regionKey,
            InstitutionId = institutionId,
            MetricFilter = storedMetricFilter,
            IsEnabled = true,
            CreatedAtUtc = DateTime.UtcNow,
            DisabledAtUtc = null,
        };

        dbContext.Subscriptions.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<SubscriptionResponse>.Success(new SubscriptionResponse(
            entity.Id,
            source.AdapterKey,
            NormalizeTypeForResponse(entity.TypeKey),
            entity.ScopeType,
            entity.RegionFilter,
            entity.InstitutionId,
            ToApiMetric(entity.MetricFilter),
            MaskTarget(entity.TypeKey, entity.Target),
            entity.IsEnabled,
            entity.CreatedAtUtc,
            entity.DisabledAtUtc));
    }

    public async Task<ServiceResult<SubscriptionResponse>> GetSubscriptionByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var projection = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(entry => entry.Id == id)
            .Select(entry => new SubscriptionProjection(
                entry.Id,
                entry.Source.AdapterKey,
                entry.TypeKey,
                entry.ScopeType,
                entry.RegionFilter,
                entry.InstitutionId,
                ToApiMetric(entry.MetricFilter),
                entry.Target,
                entry.IsEnabled,
                entry.CreatedAtUtc,
                entry.DisabledAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

        if (projection is null)
        {
            return ServiceResult<SubscriptionResponse>.Failure(
                StatusCodes.Status404NotFound,
                "Not found",
                $"Subscription '{id}' was not found.");
        }

        return ServiceResult<SubscriptionResponse>.Success(MapSubscriptionResponse(projection));
    }

    public async Task<ServiceResult<SubscriptionDeliveriesResponse>> GetSubscriptionDeliveriesAsync(
        Guid id,
        int? limit,
        CancellationToken cancellationToken)
    {
        var exists = await dbContext.Subscriptions
            .AsNoTracking()
            .AnyAsync(entry => entry.Id == id, cancellationToken);

        if (!exists)
        {
            return ServiceResult<SubscriptionDeliveriesResponse>.Failure(
                StatusCodes.Status404NotFound,
                "Not found",
                $"Subscription '{id}' was not found.");
        }

        var take = Math.Clamp(limit ?? DefaultDeliveriesLimit, 1, MaxDeliveriesLimit);

        var items = await dbContext.Deliveries
            .AsNoTracking()
            .Where(entry => entry.SubscriptionId == id)
            .OrderByDescending(entry => entry.CreatedAtUtc)
            .ThenBy(entry => entry.Id)
            .Take(take)
            .Select(entry => new SubscriptionDeliveryResponse(
                entry.EventId,
                entry.Status,
                entry.AttemptCount,
                entry.LastError,
                entry.CreatedAtUtc,
                entry.SentAtUtc))
            .ToArrayAsync(cancellationToken);

        return ServiceResult<SubscriptionDeliveriesResponse>.Success(new SubscriptionDeliveriesResponse(id, items));
    }

    public async Task<ServiceResult> DisableSubscriptionAsync(Guid id, CancellationToken cancellationToken)
    {
        var subscription = await dbContext.Subscriptions
            .SingleOrDefaultAsync(entry => entry.Id == id, cancellationToken);

        if (subscription is null)
        {
            return ServiceResult.Failure(
                StatusCodes.Status404NotFound,
                "Not found",
                $"Subscription '{id}' was not found.");
        }

        if (subscription.IsEnabled)
        {
            subscription.IsEnabled = false;
            subscription.DisabledAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return ServiceResult.Success();
    }

    private static bool IsValidTarget(string typeKey, string target)
    {
        if (string.Equals(typeKey, NotificationChannelTypeCatalog.DiscordWebhook, StringComparison.Ordinal))
        {
            return IsValidDiscordWebhookUrl(target);
        }

        if (string.Equals(typeKey, NotificationChannelTypeCatalog.TelegramChat, StringComparison.Ordinal))
        {
            return IsValidTelegramChatId(target);
        }

        return false;
    }

    private static bool IsValidDiscordWebhookUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var host = uri.Host;
        var hostAllowed = string.Equals(host, "discord.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".discord.com", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "discordapp.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".discordapp.com", StringComparison.OrdinalIgnoreCase);

        if (!hostAllowed)
        {
            return false;
        }

        var path = uri.AbsolutePath;
        if (path.StartsWith("/api/webhooks/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return VersionedWebhookPathRegex.IsMatch(path);
    }

    private static bool IsValidTelegramChatId(string value)
    {
        var normalized = NormalizeRequired(value);
        return normalized is not null && TelegramChatIdRegex.IsMatch(normalized);
    }

    private static string MaskTarget(string typeKey, string target)
    {
        var normalizedType = NormalizeTypeForResponse(typeKey);
        if (string.Equals(normalizedType, NotificationChannelTypeCatalog.DiscordWebhook, StringComparison.Ordinal))
        {
            return MaskDiscordTarget(target);
        }

        if (string.Equals(normalizedType, NotificationChannelTypeCatalog.TelegramChat, StringComparison.Ordinal))
        {
            return MaskTelegramChat(target);
        }

        return "***";
    }

    private static string MaskDiscordTarget(string target)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri))
        {
            return "***";
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 4)
        {
            var webhookId = segments[^2];
            var token = segments[^1];
            var suffix = token.Length <= 4 ? token : token[^4..];
            return $"{uri.Scheme}://{uri.Host}/api/webhooks/{webhookId}/***{suffix}";
        }

        return $"{uri.Scheme}://{uri.Host}/api/webhooks/***";
    }

    private static string MaskTelegramChat(string target)
    {
        var trimmed = NormalizeRequired(target);
        if (trimmed is null)
        {
            return "***";
        }

        var suffix = trimmed.Length <= 4 ? trimmed : trimmed[^4..];
        return $"***{suffix}";
    }

    private static string NormalizeTypeForResponse(string rawTypeKey)
    {
        return NotificationChannelTypeCatalog.TryNormalizeStored(rawTypeKey, out var normalized)
            ? normalized
            : rawTypeKey;
    }

    private static SubscriptionResponse MapSubscriptionResponse(SubscriptionProjection projection)
    {
        var normalizedType = NormalizeTypeForResponse(projection.Type);
        return new SubscriptionResponse(
            projection.Id,
            projection.Source,
            normalizedType,
            projection.ScopeType,
            projection.Region,
            projection.InstitutionId,
            projection.Metric,
            MaskTarget(normalizedType, projection.Target),
            projection.IsEnabled,
            projection.CreatedAtUtc,
            projection.DisabledAtUtc);
    }

    private static string? NormalizeRequired(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? ToApiMetric(string metricFilter)
    {
        return string.Equals(metricFilter, WildcardMetricToken, StringComparison.Ordinal)
            ? null
            : metricFilter;
    }

    private sealed record SubscriptionProjection(
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
}
