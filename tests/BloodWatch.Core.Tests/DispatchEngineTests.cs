using System.Text.Json;
using BloodWatch.Core.Contracts;
using BloodWatch.Core.Models;
using BloodWatch.Infrastructure.Persistence;
using BloodWatch.Infrastructure.Persistence.Entities;
using BloodWatch.Worker.Alerts;
using BloodWatch.Worker.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BloodWatch.Core.Tests;

public sealed class DispatchEngineTests
{
    [Fact]
    public async Task DispatchAsync_FirstCriticalAlert_ShouldSendAndOpenLowState()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedDispatchScopeAsync(
            dbContext,
            payloadJson: BuildPayloadJson(
                signal: "critical-active",
                transitionKind: "entered-critical",
                currentState: "critical",
                currentBucket: 0,
                currentUnits: 90m,
                criticalUnits: 100m));

        var notifier = new SequenceNotifier(DeliveryStatus.Sent);
        var engine = CreateEngine(dbContext, notifier);

        await engine.DispatchAsync([seeded.Event]);
        await dbContext.SaveChangesAsync();

        var delivery = await dbContext.Deliveries.SingleAsync();
        Assert.Equal("sent", delivery.Status);
        Assert.Equal(1, delivery.AttemptCount);

        var state = await dbContext.SubscriptionNotificationStates.SingleAsync();
        Assert.True(state.IsLowOpen);
        Assert.Equal(0, state.LastLowNotifiedBucket);
        Assert.Equal(90m, state.LastLowNotifiedUnits);
        Assert.NotNull(state.LastLowNotifiedAtUtc);
    }

    [Fact]
    public async Task DispatchAsync_WhenAlreadyLowAndReminderNotDue_ShouldSuppressNotification()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedDispatchScopeAsync(
            dbContext,
            payloadJson: BuildPayloadJson(
                signal: "critical-active",
                transitionKind: "still-critical",
                currentState: "critical",
                currentBucket: 0,
                currentUnits: 89m,
                criticalUnits: 100m),
            notificationState: new NotificationStateSeed(
                IsLowOpen: true,
                LastLowNotifiedAtUtc: DateTime.UtcNow.AddHours(-1),
                LastLowNotifiedBucket: 0,
                LastLowNotifiedUnits: 90m,
                LastRecoveryNotifiedAtUtc: null));

        var notifier = new SequenceNotifier(DeliveryStatus.Sent);
        var engine = CreateEngine(dbContext, notifier);

        await engine.DispatchAsync([seeded.Event]);
        await dbContext.SaveChangesAsync();

        Assert.Equal(0, notifier.CallCount);
        Assert.Equal(0, await dbContext.Deliveries.CountAsync());

        var state = await dbContext.SubscriptionNotificationStates.SingleAsync();
        Assert.True(state.IsLowOpen);
        Assert.Equal(0, state.LastLowNotifiedBucket);
        Assert.Equal(90m, state.LastLowNotifiedUnits);
    }

    [Fact]
    public async Task DispatchAsync_WhenReminderIsDue_ShouldSendReminder()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedDispatchScopeAsync(
            dbContext,
            payloadJson: BuildPayloadJson(
                signal: "critical-active",
                transitionKind: "still-critical",
                currentState: "critical",
                currentBucket: 0,
                currentUnits: 88m,
                criticalUnits: 100m),
            notificationState: new NotificationStateSeed(
                IsLowOpen: true,
                LastLowNotifiedAtUtc: DateTime.UtcNow.AddHours(-25),
                LastLowNotifiedBucket: 0,
                LastLowNotifiedUnits: 90m,
                LastRecoveryNotifiedAtUtc: null));

        var notifier = new SequenceNotifier(DeliveryStatus.Sent);
        var engine = CreateEngine(dbContext, notifier);

        await engine.DispatchAsync([seeded.Event]);
        await dbContext.SaveChangesAsync();

        var delivery = await dbContext.Deliveries.SingleAsync();
        Assert.Equal("sent", delivery.Status);
        Assert.Equal(1, notifier.CallCount);
        Assert.Equal("critical-reminder", ReadPayloadField(notifier.ReceivedEvents.Single().PayloadJson, "notificationKind"));

        var state = await dbContext.SubscriptionNotificationStates.SingleAsync();
        Assert.True(state.IsLowOpen);
        Assert.Equal(0, state.LastLowNotifiedBucket);
        Assert.Equal(88m, state.LastLowNotifiedUnits);
    }

    [Fact]
    public async Task DispatchAsync_WhenBucketWorsens_ShouldSendImmediateWorseningAlert()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedDispatchScopeAsync(
            dbContext,
            payloadJson: BuildPayloadJson(
                signal: "critical-active",
                transitionKind: "still-critical",
                currentState: "critical",
                currentBucket: 2,
                currentUnits: 70m,
                criticalUnits: 100m),
            notificationState: new NotificationStateSeed(
                IsLowOpen: true,
                LastLowNotifiedAtUtc: DateTime.UtcNow.AddHours(-1),
                LastLowNotifiedBucket: 0,
                LastLowNotifiedUnits: 90m,
                LastRecoveryNotifiedAtUtc: null));

        var notifier = new SequenceNotifier(DeliveryStatus.Sent);
        var engine = CreateEngine(dbContext, notifier);

        await engine.DispatchAsync([seeded.Event]);
        await dbContext.SaveChangesAsync();

        Assert.Equal(1, notifier.CallCount);
        Assert.Equal("critical-worsening", ReadPayloadField(notifier.ReceivedEvents.Single().PayloadJson, "notificationKind"));

        var state = await dbContext.SubscriptionNotificationStates.SingleAsync();
        Assert.True(state.IsLowOpen);
        Assert.Equal(2, state.LastLowNotifiedBucket);
        Assert.Equal(70m, state.LastLowNotifiedUnits);
    }

    [Fact]
    public async Task DispatchAsync_WhenRecoverySignal_ShouldSendAndCloseLowState()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedDispatchScopeAsync(
            dbContext,
            payloadJson: BuildPayloadJson(
                signal: "recovery",
                transitionKind: "recovered-from-critical",
                currentState: "normal",
                currentBucket: null,
                currentUnits: 130m,
                criticalUnits: 100m),
            notificationState: new NotificationStateSeed(
                IsLowOpen: true,
                LastLowNotifiedAtUtc: DateTime.UtcNow.AddHours(-3),
                LastLowNotifiedBucket: 1,
                LastLowNotifiedUnits: 85m,
                LastRecoveryNotifiedAtUtc: null));

        var notifier = new SequenceNotifier(DeliveryStatus.Sent);
        var engine = CreateEngine(dbContext, notifier);

        await engine.DispatchAsync([seeded.Event]);
        await dbContext.SaveChangesAsync();

        Assert.Equal(1, notifier.CallCount);
        Assert.Equal("recovery", ReadPayloadField(notifier.ReceivedEvents.Single().PayloadJson, "notificationKind"));

        var delivery = await dbContext.Deliveries.SingleAsync();
        Assert.Equal("sent", delivery.Status);

        var state = await dbContext.SubscriptionNotificationStates.SingleAsync();
        Assert.False(state.IsLowOpen);
        Assert.Null(state.LastLowNotifiedBucket);
        Assert.Null(state.LastLowNotifiedUnits);
        Assert.NotNull(state.LastRecoveryNotifiedAtUtc);
    }

    [Fact]
    public async Task DispatchAsync_WhenRecoverySendFails_ShouldStillCloseLowState()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedDispatchScopeAsync(
            dbContext,
            payloadJson: BuildPayloadJson(
                signal: "recovery",
                transitionKind: "recovered-from-critical",
                currentState: "warning",
                currentBucket: null,
                currentUnits: 110m,
                criticalUnits: 100m),
            notificationState: new NotificationStateSeed(
                IsLowOpen: true,
                LastLowNotifiedAtUtc: DateTime.UtcNow.AddHours(-3),
                LastLowNotifiedBucket: 1,
                LastLowNotifiedUnits: 85m,
                LastRecoveryNotifiedAtUtc: null));

        var notifier = new SequenceNotifier(
            DeliveryStatus.Failed,
            DeliveryStatus.Failed,
            DeliveryStatus.Failed);
        var engine = CreateEngine(dbContext, notifier);

        await engine.DispatchAsync([seeded.Event]);
        await dbContext.SaveChangesAsync();

        var delivery = await dbContext.Deliveries.SingleAsync();
        Assert.Equal("failed", delivery.Status);
        Assert.Equal(3, delivery.AttemptCount);
        Assert.NotNull(delivery.LastError);

        var state = await dbContext.SubscriptionNotificationStates.SingleAsync();
        Assert.False(state.IsLowOpen);
        Assert.Null(state.LastLowNotifiedBucket);
        Assert.Null(state.LastLowNotifiedUnits);
    }

    [Fact]
    public async Task DispatchAsync_WhenCriticalSendFails_ShouldKeepLowStateClosed()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedDispatchScopeAsync(
            dbContext,
            payloadJson: BuildPayloadJson(
                signal: "critical-active",
                transitionKind: "entered-critical",
                currentState: "critical",
                currentBucket: 0,
                currentUnits: 90m,
                criticalUnits: 100m));

        var notifier = new SequenceNotifier(
            DeliveryStatus.Failed,
            DeliveryStatus.Failed,
            DeliveryStatus.Failed);
        var engine = CreateEngine(dbContext, notifier);

        await engine.DispatchAsync([seeded.Event]);
        await dbContext.SaveChangesAsync();

        var delivery = await dbContext.Deliveries.SingleAsync();
        Assert.Equal("failed", delivery.Status);
        Assert.Equal(3, delivery.AttemptCount);

        var state = await dbContext.SubscriptionNotificationStates.SingleAsync();
        Assert.False(state.IsLowOpen);
        Assert.Null(state.LastLowNotifiedAtUtc);
        Assert.Null(state.LastLowNotifiedBucket);
        Assert.Null(state.LastLowNotifiedUnits);
    }

    private static DispatchEngine CreateEngine(BloodWatchDbContext dbContext, SequenceNotifier notifier)
    {
        return new DispatchEngine(
            dbContext,
            [notifier],
            NullLogger<DispatchEngine>.Instance,
            Options.Create(new AlertThresholdOptions
            {
                ReminderIntervalHours = 24,
                WorseningBucketDelta = 1,
                SendRecoveryNotification = true,
            }));
    }

    private static BloodWatchDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<BloodWatchDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        var dbContext = new BloodWatchDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static async Task<SeededDispatchScope> SeedDispatchScopeAsync(
        BloodWatchDbContext dbContext,
        string payloadJson,
        NotificationStateSeed? notificationState = null)
    {
        var source = new SourceEntity
        {
            Id = Guid.NewGuid(),
            AdapterKey = "pt-transparencia-sns",
            Name = "Portugal SNS Transparency",
            CreatedAtUtc = DateTime.UtcNow,
        };

        var region = new RegionEntity
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            Key = "pt-norte",
            DisplayName = "Regiao de Saude Norte",
            CreatedAtUtc = DateTime.UtcNow,
        };

        var reserve = new CurrentReserveEntity
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            RegionId = region.Id,
            MetricKey = "overall",
            Value = 90m,
            Unit = "units",
            Severity = null,
            ReferenceDate = new DateOnly(2026, 2, 1),
            CapturedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

        var @event = new EventEntity
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            CurrentReserveId = reserve.Id,
            RegionId = region.Id,
            RuleKey = "low-stock-threshold.v1",
            MetricKey = "overall",
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            PayloadJson = payloadJson,
            CreatedAtUtc = DateTime.UtcNow,
        };

        var subscription = new SubscriptionEntity
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            TypeKey = "discord-webhook",
            Target = "https://discord.com/api/webhooks/123/token",
            RegionFilter = "pt-norte",
            MetricFilter = "overall",
            IsEnabled = true,
            CreatedAtUtc = DateTime.UtcNow,
        };

        dbContext.Sources.Add(source);
        dbContext.Regions.Add(region);
        dbContext.CurrentReserves.Add(reserve);
        dbContext.Events.Add(@event);
        dbContext.Subscriptions.Add(subscription);

        if (notificationState is not null)
        {
            dbContext.SubscriptionNotificationStates.Add(new SubscriptionNotificationStateEntity
            {
                SubscriptionId = subscription.Id,
                IsLowOpen = notificationState.IsLowOpen,
                LastLowNotifiedAtUtc = notificationState.LastLowNotifiedAtUtc,
                LastLowNotifiedBucket = notificationState.LastLowNotifiedBucket,
                LastLowNotifiedUnits = notificationState.LastLowNotifiedUnits,
                LastRecoveryNotifiedAtUtc = notificationState.LastRecoveryNotifiedAtUtc,
                UpdatedAtUtc = DateTime.UtcNow,
            });
        }

        await dbContext.SaveChangesAsync();

        return new SeededDispatchScope(@event);
    }

    private static string BuildPayloadJson(
        string signal,
        string transitionKind,
        string currentState,
        int? currentBucket,
        decimal currentUnits,
        decimal criticalUnits)
    {
        return JsonSerializer.Serialize(new
        {
            source = "pt-transparencia-sns",
            region = "pt-norte",
            metric = "overall",
            signal,
            transitionKind,
            currentState,
            currentCriticalBucket = currentBucket,
            currentUnits,
            criticalUnits,
            capturedAtUtc = DateTime.UtcNow,
        });
    }

    private static string? ReadPayloadField(string payloadJson, string fieldName)
    {
        using var document = JsonDocument.Parse(payloadJson);
        return document.RootElement.TryGetProperty(fieldName, out var property)
            ? property.GetString()
            : null;
    }

    private sealed record SeededDispatchScope(EventEntity Event);

    private sealed record NotificationStateSeed(
        bool IsLowOpen,
        DateTime? LastLowNotifiedAtUtc,
        int? LastLowNotifiedBucket,
        decimal? LastLowNotifiedUnits,
        DateTime? LastRecoveryNotifiedAtUtc);

    private sealed class SequenceNotifier(params DeliveryStatus[] statuses) : INotifier
    {
        private readonly Queue<DeliveryStatus> _statuses = new(statuses);

        public string TypeKey => "discord-webhook";
        public int CallCount { get; private set; }
        public List<Event> ReceivedEvents { get; } = [];

        public Task<Delivery> SendAsync(Event @event, string target, CancellationToken cancellationToken = default)
        {
            CallCount++;
            ReceivedEvents.Add(@event);

            var status = _statuses.Count > 0 ? _statuses.Dequeue() : DeliveryStatus.Failed;
            return Task.FromResult(new Delivery(
                TypeKey,
                target,
                status,
                DateTime.UtcNow,
                LastError: status == DeliveryStatus.Failed ? "send failed" : null,
                SentAtUtc: status == DeliveryStatus.Sent ? DateTime.UtcNow : null));
        }
    }
}
