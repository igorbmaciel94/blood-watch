using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using BloodWatch.Core.Contracts;
using BloodWatch.Core.Models;
using BloodWatch.Infrastructure.Persistence;
using BloodWatch.Infrastructure.Persistence.Entities;
using BloodWatch.Worker.Alerts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BloodWatch.Worker.Dispatch;

public sealed class DispatchEngine(
    BloodWatchDbContext dbContext,
    IEnumerable<INotifier> notifiers,
    ILogger<DispatchEngine> logger,
    IOptions<AlertThresholdOptions> alertOptions)
{
    private const int MaxAttempts = 3;

    private readonly BloodWatchDbContext _dbContext = dbContext;
    private readonly IReadOnlyDictionary<string, INotifier> _notifiersByType = notifiers
        .GroupBy(notifier => notifier.TypeKey, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    private readonly ILogger<DispatchEngine> _logger = logger;
    private readonly AlertThresholdOptions _alertOptions = alertOptions.Value;

    public async Task<int> DispatchAsync(
        IReadOnlyCollection<EventEntity> events,
        CancellationToken cancellationToken = default)
    {
        if (events.Count == 0)
        {
            return 0;
        }

        var eventIds = events.Select(entry => entry.Id).Distinct().ToArray();
        var sourceIds = events.Select(entry => entry.SourceId).Distinct().ToArray();
        var regionIds = events
            .Where(entry => entry.RegionId.HasValue)
            .Select(entry => entry.RegionId!.Value)
            .Distinct()
            .ToArray();
        var reserveIds = events.Select(entry => entry.CurrentReserveId).Distinct().ToArray();

        var subscriptions = await _dbContext.Subscriptions
            .AsNoTracking()
            .Where(subscription => subscription.IsEnabled && sourceIds.Contains(subscription.SourceId))
            .ToListAsync(cancellationToken);

        if (subscriptions.Count == 0)
        {
            return 0;
        }

        var subscriptionsByScope = subscriptions
            .GroupBy(
                subscription => new SubscriptionScope(subscription.SourceId, subscription.RegionFilter, subscription.MetricFilter),
                SubscriptionScope.Comparer)
            .ToDictionary(group => group.Key, group => group.ToArray(), SubscriptionScope.Comparer);

        var sourcesById = await _dbContext.Sources
            .AsNoTracking()
            .Where(entry => sourceIds.Contains(entry.Id))
            .ToDictionaryAsync(entry => entry.Id, entry => entry, cancellationToken);

        var regionsById = await _dbContext.Regions
            .AsNoTracking()
            .Where(entry => regionIds.Contains(entry.Id))
            .ToDictionaryAsync(entry => entry.Id, entry => entry, cancellationToken);

        var reserveUnitsById = await _dbContext.CurrentReserves
            .AsNoTracking()
            .Where(entry => reserveIds.Contains(entry.Id))
            .ToDictionaryAsync(entry => entry.Id, entry => entry.Unit, cancellationToken);

        var existingDeliveries = await _dbContext.Deliveries
            .Where(delivery => eventIds.Contains(delivery.EventId))
            .ToListAsync(cancellationToken);

        var deliveriesByKey = existingDeliveries.ToDictionary(
            delivery => new DeliveryKey(delivery.EventId, delivery.SubscriptionId),
            delivery => delivery,
            DeliveryKey.Comparer);

        var subscriptionIds = subscriptions.Select(entry => entry.Id).Distinct().ToArray();
        var existingNotificationStates = await _dbContext.SubscriptionNotificationStates
            .Where(entry => subscriptionIds.Contains(entry.SubscriptionId))
            .ToListAsync(cancellationToken);

        var notificationStateBySubscriptionId = existingNotificationStates.ToDictionary(
            entry => entry.SubscriptionId,
            entry => entry);

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

            var scope = new SubscriptionScope(source.Id, region.Key, eventEntity.MetricKey);
            if (!subscriptionsByScope.TryGetValue(scope, out var scopeSubscriptions) || scopeSubscriptions.Length == 0)
            {
                continue;
            }

            var unit = reserveUnitsById.TryGetValue(eventEntity.CurrentReserveId, out var knownUnit)
                ? knownUnit
                : "units";

            var modelEvent = new Event(
                eventEntity.RuleKey,
                new SourceRef(source.AdapterKey, source.Name),
                new Metric(eventEntity.MetricKey, eventEntity.MetricKey, unit),
                new RegionRef(region.Key, region.DisplayName),
                eventEntity.CreatedAtUtc,
                eventEntity.PayloadJson);

            var payload = ParsePayload(eventEntity.PayloadJson);

            foreach (var subscription in scopeSubscriptions)
            {
                var state = GetOrCreateNotificationState(notificationStateBySubscriptionId, subscription.Id);
                var nowUtc = DateTime.UtcNow;
                var decision = ResolveDispatchDecision(payload, state, nowUtc);
                if (!decision.ShouldSend)
                {
                    if (decision.CloseLowEpisode)
                    {
                        CloseLowEpisode(state, nowUtc, null);
                    }

                    continue;
                }

                var deliveryKey = new DeliveryKey(eventEntity.Id, subscription.Id);
                var delivery = GetOrCreateDelivery(deliveriesByKey, deliveryKey, eventEntity.Id, subscription.Id);

                if (string.Equals(delivery.Status, "sent", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var dispatchEvent = WithNotificationKind(modelEvent, decision.NotificationKind);
                var wasSent = false;

                if (!_notifiersByType.TryGetValue(subscription.TypeKey, out var notifier))
                {
                    delivery.AttemptCount = 0;
                    delivery.Status = "failed";
                    delivery.LastError = $"No notifier registered for type '{subscription.TypeKey}'.";
                    delivery.SentAtUtc = null;
                }
                else
                {
                    wasSent = await SendWithRetriesAsync(notifier, dispatchEvent, subscription, delivery, cancellationToken);
                    if (wasSent)
                    {
                        sentCount++;
                    }
                }

                if (string.Equals(payload.Signal, "critical-active", StringComparison.Ordinal))
                {
                    if (wasSent)
                    {
                        MarkLowNotificationSent(state, payload, delivery.SentAtUtc ?? DateTime.UtcNow);
                    }

                    continue;
                }

                if (string.Equals(payload.Signal, "recovery", StringComparison.Ordinal))
                {
                    DateTime? recoverySentAtUtc = wasSent ? (delivery.SentAtUtc ?? DateTime.UtcNow) : null;
                    CloseLowEpisode(state, DateTime.UtcNow, recoverySentAtUtc);
                }
            }
        }

        return sentCount;
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

    private SubscriptionNotificationStateEntity GetOrCreateNotificationState(
        IDictionary<Guid, SubscriptionNotificationStateEntity> stateBySubscriptionId,
        Guid subscriptionId)
    {
        if (stateBySubscriptionId.TryGetValue(subscriptionId, out var existing))
        {
            return existing;
        }

        var state = new SubscriptionNotificationStateEntity
        {
            SubscriptionId = subscriptionId,
            IsLowOpen = false,
            LastLowNotifiedAtUtc = null,
            LastLowNotifiedBucket = null,
            LastLowNotifiedUnits = null,
            LastRecoveryNotifiedAtUtc = null,
            UpdatedAtUtc = DateTime.UtcNow,
        };

        _dbContext.SubscriptionNotificationStates.Add(state);
        stateBySubscriptionId[subscriptionId] = state;
        return state;
    }

    private DispatchDecision ResolveDispatchDecision(
        EventPayload payload,
        SubscriptionNotificationStateEntity state,
        DateTime nowUtc)
    {
        if (string.Equals(payload.Signal, "critical-active", StringComparison.Ordinal))
        {
            if (!state.IsLowOpen)
            {
                return new DispatchDecision(true, "critical-alert", false);
            }

            if (payload.CurrentCriticalBucket.HasValue
                && state.LastLowNotifiedBucket.HasValue
                && payload.CurrentCriticalBucket.Value >= state.LastLowNotifiedBucket.Value + ResolveWorseningBucketDelta())
            {
                return new DispatchDecision(true, "critical-worsening", false);
            }

            if (!state.LastLowNotifiedAtUtc.HasValue
                || nowUtc - state.LastLowNotifiedAtUtc.Value >= ResolveReminderInterval())
            {
                return new DispatchDecision(true, "critical-reminder", false);
            }

            return DispatchDecision.Skip;
        }

        if (string.Equals(payload.Signal, "recovery", StringComparison.Ordinal))
        {
            if (!state.IsLowOpen)
            {
                return DispatchDecision.Skip;
            }

            if (!_alertOptions.SendRecoveryNotification)
            {
                return new DispatchDecision(false, null, true);
            }

            return new DispatchDecision(true, "recovery", true);
        }

        return DispatchDecision.Skip;
    }

    private static void MarkLowNotificationSent(
        SubscriptionNotificationStateEntity state,
        EventPayload payload,
        DateTime sentAtUtc)
    {
        state.IsLowOpen = true;
        state.LastLowNotifiedAtUtc = sentAtUtc;
        state.LastLowNotifiedBucket = payload.CurrentCriticalBucket;
        state.LastLowNotifiedUnits = payload.CurrentUnits;
        state.UpdatedAtUtc = sentAtUtc;
    }

    private static void CloseLowEpisode(
        SubscriptionNotificationStateEntity state,
        DateTime nowUtc,
        DateTime? recoveryNotifiedAtUtc)
    {
        state.IsLowOpen = false;
        state.LastLowNotifiedBucket = null;
        state.LastLowNotifiedUnits = null;
        state.UpdatedAtUtc = nowUtc;

        if (recoveryNotifiedAtUtc.HasValue)
        {
            state.LastRecoveryNotifiedAtUtc = recoveryNotifiedAtUtc.Value;
        }
    }

    private static Event WithNotificationKind(Event @event, string? notificationKind)
    {
        if (string.IsNullOrWhiteSpace(notificationKind))
        {
            return @event;
        }

        try
        {
            var node = JsonNode.Parse(@event.PayloadJson) as JsonObject ?? new JsonObject();
            node["notificationKind"] = notificationKind;
            return @event with { PayloadJson = node.ToJsonString() };
        }
        catch (JsonException)
        {
            return @event with { PayloadJson = JsonSerializer.Serialize(new { notificationKind }) };
        }
    }

    private static EventPayload ParsePayload(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;

            var signal = ReadString(root, "signal");
            var transitionKind = ReadString(root, "transitionKind");
            var currentState = ReadString(root, "currentState");
            var currentCriticalBucket = ReadInt(root, "currentCriticalBucket");
            var currentUnits = ReadDecimal(root, "currentUnits");

            signal ??= ResolveLegacySignal(transitionKind);

            return new EventPayload(
                Signal: signal ?? "unknown",
                TransitionKind: transitionKind ?? "unknown",
                CurrentState: currentState ?? "unknown",
                CurrentCriticalBucket: currentCriticalBucket,
                CurrentUnits: currentUnits);
        }
        catch (JsonException)
        {
            return new EventPayload(
                Signal: "unknown",
                TransitionKind: "unknown",
                CurrentState: "unknown",
                CurrentCriticalBucket: null,
                CurrentUnits: null);
        }
    }

    private static string? ResolveLegacySignal(string? transitionKind)
    {
        if (string.IsNullOrWhiteSpace(transitionKind))
        {
            return null;
        }

        if (transitionKind.Contains("recover", StringComparison.OrdinalIgnoreCase))
        {
            return "recovery";
        }

        if (transitionKind.Contains("critical", StringComparison.OrdinalIgnoreCase))
        {
            return "critical-active";
        }

        return null;
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static int? ReadInt(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var intValue))
        {
            return intValue;
        }

        if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out var parsedValue))
        {
            return parsedValue;
        }

        return null;
    }

    private static decimal? ReadDecimal(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var decimalValue))
        {
            return decimalValue;
        }

        if (property.ValueKind == JsonValueKind.String
            && decimal.TryParse(property.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedValue))
        {
            return parsedValue;
        }

        return null;
    }

    private TimeSpan ResolveReminderInterval()
    {
        var hours = Math.Clamp(_alertOptions.ReminderIntervalHours, 1, 24 * 30);
        return TimeSpan.FromHours(hours);
    }

    private int ResolveWorseningBucketDelta()
    {
        return Math.Max(1, _alertOptions.WorseningBucketDelta);
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

    private sealed record SubscriptionScope(Guid SourceId, string RegionKey, string MetricKey)
    {
        public static IEqualityComparer<SubscriptionScope> Comparer { get; } = new SubscriptionScopeComparer();

        private sealed class SubscriptionScopeComparer : IEqualityComparer<SubscriptionScope>
        {
            public bool Equals(SubscriptionScope? x, SubscriptionScope? y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }

                if (x is null || y is null)
                {
                    return false;
                }

                return x.SourceId == y.SourceId
                    && string.Equals(x.RegionKey, y.RegionKey, StringComparison.Ordinal)
                    && string.Equals(x.MetricKey, y.MetricKey, StringComparison.Ordinal);
            }

            public int GetHashCode(SubscriptionScope obj)
            {
                return HashCode.Combine(
                    obj.SourceId,
                    StringComparer.Ordinal.GetHashCode(obj.RegionKey),
                    StringComparer.Ordinal.GetHashCode(obj.MetricKey));
            }
        }
    }

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

    private sealed record EventPayload(
        string Signal,
        string TransitionKind,
        string CurrentState,
        int? CurrentCriticalBucket,
        decimal? CurrentUnits);

    private sealed record DispatchDecision(bool ShouldSend, string? NotificationKind, bool CloseLowEpisode)
    {
        public static DispatchDecision Skip { get; } = new(false, null, false);
    }
}
