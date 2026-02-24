using System.Text.Json;
using System.Diagnostics;
using BloodWatch.Core.Contracts;
using BloodWatch.Core.Models;
using BloodWatch.Infrastructure.Persistence;
using BloodWatch.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BloodWatch.Worker.Dispatch;

public sealed class DispatchEngine(
    BloodWatchDbContext dbContext,
    IEnumerable<INotifier> notifiers,
    ILogger<DispatchEngine> logger)
{
    private const int MaxAttempts = 3;
    private const string WildcardMetricToken = "*";
    private const string StatusPresenceTransitionKind = "non-normal-presence";

    private readonly BloodWatchDbContext _dbContext = dbContext;
    private readonly IReadOnlyDictionary<string, INotifier> _notifiersByType = notifiers
        .GroupBy(notifier => notifier.TypeKey, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    private readonly ILogger<DispatchEngine> _logger = logger;

    public async Task<int> DispatchAsync(
        IReadOnlyCollection<EventEntity> events,
        CancellationToken cancellationToken = default)
    {
        var dispatchStopwatch = Stopwatch.StartNew();
        if (events.Count == 0)
        {
            _logger.LogInformation("Dispatch skipped because there are no candidate events.");
            return 0;
        }

        var eventIds = events.Select(entry => entry.Id).Distinct().ToArray();
        var sourceIds = events.Select(entry => entry.SourceId).Distinct().ToArray();
        var regionIds = events
            .Where(entry => entry.RegionId.HasValue)
            .Select(entry => entry.RegionId!.Value)
            .Distinct()
            .ToArray();

        var subscriptions = await _dbContext.Subscriptions
            .AsNoTracking()
            .Where(subscription => subscription.IsEnabled && sourceIds.Contains(subscription.SourceId))
            .ToListAsync(cancellationToken);

        if (subscriptions.Count == 0)
        {
            return 0;
        }

        var sourcesById = await _dbContext.Sources
            .AsNoTracking()
            .Where(entry => sourceIds.Contains(entry.Id))
            .ToDictionaryAsync(entry => entry.Id, entry => entry, cancellationToken);

        var regionsById = await _dbContext.Regions
            .AsNoTracking()
            .Where(entry => regionIds.Contains(entry.Id))
            .ToDictionaryAsync(entry => entry.Id, entry => entry, cancellationToken);

        var institutionIds = subscriptions
            .Where(subscription => subscription.InstitutionId.HasValue)
            .Select(subscription => subscription.InstitutionId!.Value)
            .Distinct()
            .ToArray();

        var institutionsById = await _dbContext.DonationCenters
            .AsNoTracking()
            .Where(center => institutionIds.Contains(center.Id))
            .ToDictionaryAsync(center => center.Id, center => center, cancellationToken);

        var existingDeliveries = await _dbContext.Deliveries
            .Where(delivery => eventIds.Contains(delivery.EventId))
            .ToListAsync(cancellationToken);

        var deliveriesByKey = existingDeliveries.ToDictionary(
            delivery => new DeliveryKey(delivery.EventId, delivery.SubscriptionId),
            delivery => delivery,
            DeliveryKey.Comparer);

        var subscriptionIds = subscriptions
            .Select(subscription => subscription.Id)
            .Distinct()
            .ToArray();

        var sentScopeMetricKeys = await LoadSentScopeMetricKeysAsync(
            subscriptionIds,
            sourceIds,
            cancellationToken);

        var dispatchMetadataByEventId = events
            .DistinctBy(eventEntity => eventEntity.Id)
            .ToDictionary(
                eventEntity => eventEntity.Id,
                eventEntity => ParseDispatchMetadata(eventEntity.PayloadJson));

        var sentCount = 0;

        foreach (var eventEntity in events)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!sourcesById.TryGetValue(eventEntity.SourceId, out var source))
            {
                continue;
            }

            if (!eventEntity.RegionId.HasValue || !regionsById.TryGetValue(eventEntity.RegionId.Value, out var region))
            {
                continue;
            }

            var scopeSubscriptions = subscriptions
                .Where(subscription => MatchesScope(subscription, eventEntity, region, institutionsById))
                .ToArray();

            if (scopeSubscriptions.Length == 0)
            {
                continue;
            }

            var dispatchMetadata = dispatchMetadataByEventId[eventEntity.Id];
            var modelEvent = new Event(
                eventEntity.RuleKey,
                new SourceRef(source.AdapterKey, source.Name),
                new Metric(eventEntity.MetricKey, eventEntity.MetricKey),
                new RegionRef(region.Key, region.DisplayName),
                eventEntity.CreatedAtUtc,
                eventEntity.PayloadJson);

            foreach (var subscription in scopeSubscriptions)
            {
                var scopeMetricKey = new SubscriptionScopeMetricKey(subscription.Id, region.Id, eventEntity.MetricKey);
                if (dispatchMetadata.IsStatusPresenceEvent && sentScopeMetricKeys.Contains(scopeMetricKey))
                {
                    continue;
                }

                var deliveryKey = new DeliveryKey(eventEntity.Id, subscription.Id);
                var delivery = GetOrCreateDelivery(deliveriesByKey, deliveryKey, eventEntity.Id, subscription.Id);

                if (string.Equals(delivery.Status, "sent", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!_notifiersByType.TryGetValue(subscription.TypeKey, out var notifier))
                {
                    if (NotificationChannelTypeCatalog.TryNormalizeStored(subscription.TypeKey, out var normalizedTypeKey)
                        && _notifiersByType.TryGetValue(normalizedTypeKey, out var normalizedNotifier))
                    {
                        notifier = normalizedNotifier;
                    }
                    else
                    {
                        delivery.AttemptCount = 0;
                        delivery.Status = "failed";
                        delivery.LastError = $"No notifier registered for type '{subscription.TypeKey}'.";
                        delivery.SentAtUtc = null;
                        continue;
                    }
                }

                var wasSent = await SendWithRetriesAsync(notifier, modelEvent, subscription, delivery, cancellationToken);
                if (wasSent)
                {
                    sentScopeMetricKeys.Add(scopeMetricKey);
                    sentCount++;
                }
            }
        }

        dispatchStopwatch.Stop();
        _logger.LogInformation(
            "Dispatch engine finished in {DurationMs}ms for {EventCount} candidate events and {SentCount} sent deliveries.",
            dispatchStopwatch.ElapsedMilliseconds,
            events.Count,
            sentCount);

        return sentCount;
    }

    private static bool MatchesScope(
        SubscriptionEntity subscription,
        EventEntity eventEntity,
        RegionEntity region,
        IReadOnlyDictionary<Guid, DonationCenterEntity> institutionsById)
    {
        var isWildcardMetric = string.Equals(subscription.MetricFilter, WildcardMetricToken, StringComparison.Ordinal);
        if (!isWildcardMetric
            && !string.Equals(subscription.MetricFilter, eventEntity.MetricKey, StringComparison.Ordinal))
        {
            return false;
        }

        if (string.Equals(subscription.ScopeType, "region", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(subscription.RegionFilter, region.Key, StringComparison.Ordinal);
        }

        if (!string.Equals(subscription.ScopeType, "institution", StringComparison.OrdinalIgnoreCase)
            || !subscription.InstitutionId.HasValue)
        {
            return false;
        }

        if (!institutionsById.TryGetValue(subscription.InstitutionId.Value, out var institution))
        {
            return false;
        }

        return institution.RegionId == region.Id;
    }

    private DeliveryEntity GetOrCreateDelivery(
        IDictionary<DeliveryKey, DeliveryEntity> deliveriesByKey,
        DeliveryKey key,
        Guid eventId,
        Guid subscriptionId)
    {
        if (deliveriesByKey.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var delivery = new DeliveryEntity
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            SubscriptionId = subscriptionId,
            AttemptCount = 0,
            Status = "pending",
            LastError = null,
            CreatedAtUtc = DateTime.UtcNow,
            SentAtUtc = null,
        };

        _dbContext.Deliveries.Add(delivery);
        deliveriesByKey[key] = delivery;

        return delivery;
    }

    private async Task<HashSet<SubscriptionScopeMetricKey>> LoadSentScopeMetricKeysAsync(
        IReadOnlyCollection<Guid> subscriptionIds,
        IReadOnlyCollection<Guid> sourceIds,
        CancellationToken cancellationToken)
    {
        if (subscriptionIds.Count == 0 || sourceIds.Count == 0)
        {
            return [];
        }

        var rows = await (
                from delivery in _dbContext.Deliveries.AsNoTracking()
                join eventEntity in _dbContext.Events.AsNoTracking() on delivery.EventId equals eventEntity.Id
                where delivery.Status == "sent"
                      && subscriptionIds.Contains(delivery.SubscriptionId)
                      && sourceIds.Contains(eventEntity.SourceId)
                      && eventEntity.RegionId.HasValue
                select new
                {
                    delivery.SubscriptionId,
                    RegionId = eventEntity.RegionId!.Value,
                    eventEntity.MetricKey,
                })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new SubscriptionScopeMetricKey(row.SubscriptionId, row.RegionId, row.MetricKey))
            .ToHashSet();
    }

    private async Task<bool> SendWithRetriesAsync(
        INotifier notifier,
        Event modelEvent,
        SubscriptionEntity subscription,
        DeliveryEntity delivery,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            delivery.AttemptCount = attempt;

            try
            {
                var result = await notifier.SendAsync(modelEvent, subscription.Target, cancellationToken);

                if (result.Status == DeliveryStatus.Sent)
                {
                    delivery.Status = "sent";
                    delivery.LastError = null;
                    delivery.SentAtUtc = result.SentAtUtc ?? DateTime.UtcNow;
                    return true;
                }

                delivery.LastError = TrimError(result.LastError) ?? "Notifier returned failed delivery status.";
                if (result.FailureKind == DeliveryFailureKind.Permanent)
                {
                    delivery.Status = "failed";
                    delivery.SentAtUtc = null;
                    return false;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                delivery.LastError = TrimError(ex.Message) ?? "Unexpected dispatch error.";
                _logger.LogWarning(
                    ex,
                    "Delivery attempt {Attempt}/{MaxAttempts} failed for event {EventId} and subscription {SubscriptionId} ({Target}).",
                    attempt,
                    MaxAttempts,
                    delivery.EventId,
                    subscription.Id,
                    MaskTarget(subscription.Target));
            }

            if (attempt < MaxAttempts)
            {
                await Task.Delay(ResolveBackoff(attempt), cancellationToken);
            }
        }

        delivery.Status = "failed";
        delivery.SentAtUtc = null;
        delivery.LastError ??= "Delivery failed after retry attempts.";

        return false;
    }

    private static TimeSpan ResolveBackoff(int attempt)
    {
        return attempt switch
        {
            1 => TimeSpan.FromMilliseconds(500),
            2 => TimeSpan.FromSeconds(1),
            _ => TimeSpan.FromSeconds(2),
        };
    }

    private static string? TrimError(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        const int maxLength = 1024;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : trimmed[..maxLength];
    }

    private static string MaskTarget(string target)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri))
        {
            return "***";
        }

        return $"{uri.Scheme}://{uri.Host}/api/webhooks/***";
    }

    private static DispatchMetadata ParseDispatchMetadata(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            var transitionKind = TryReadString(root, "transitionKind");

            return new DispatchMetadata(
                IsStatusPresenceEvent: string.Equals(
                    transitionKind,
                    StatusPresenceTransitionKind,
                    StringComparison.Ordinal));
        }
        catch (JsonException)
        {
            return new DispatchMetadata(IsStatusPresenceEvent: false);
        }
    }

    private static string? TryReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private sealed record DispatchMetadata(bool IsStatusPresenceEvent);

    private sealed record SubscriptionScopeMetricKey(Guid SubscriptionId, Guid RegionId, string MetricKey);

    private sealed record DeliveryKey(Guid EventId, Guid SubscriptionId)
    {
        public static IEqualityComparer<DeliveryKey> Comparer { get; } = new DeliveryKeyComparer();

        private sealed class DeliveryKeyComparer : IEqualityComparer<DeliveryKey>
        {
            public bool Equals(DeliveryKey? x, DeliveryKey? y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }

                if (x is null || y is null)
                {
                    return false;
                }

                return x.EventId == y.EventId && x.SubscriptionId == y.SubscriptionId;
            }

            public int GetHashCode(DeliveryKey obj)
            {
                return HashCode.Combine(obj.EventId, obj.SubscriptionId);
            }
        }
    }
}
