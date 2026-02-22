using System.Text.Json;
using BloodWatch.Core.Contracts;
using BloodWatch.Core.Models;
using BloodWatch.Infrastructure.Persistence;
using BloodWatch.Infrastructure.Persistence.Entities;
using BloodWatch.Worker.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BloodWatch.Core.Tests;

public sealed class DispatchEngineTests
{
    [Fact]
    public async Task DispatchAsync_RegionScopeSubscription_ShouldSendNotification()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedDispatchScopeAsync(dbContext, addInstitutionSubscription: false);

        var notifier = new SequenceNotifier(DeliveryStatus.Sent);
        var engine = CreateEngine(dbContext, notifier);

        var sentCount = await engine.DispatchAsync([seeded.Event]);
        await dbContext.SaveChangesAsync();

        Assert.Equal(1, sentCount);
        Assert.Equal(1, notifier.CallCount);

        var delivery = await dbContext.Deliveries.SingleAsync();
        Assert.Equal("sent", delivery.Status);
        Assert.Equal(1, delivery.AttemptCount);
    }

    [Fact]
    public async Task DispatchAsync_InstitutionScopeSubscription_ShouldSendWhenInstitutionRegionMatchesEventRegion()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedDispatchScopeAsync(dbContext, addInstitutionSubscription: true);

        var notifier = new SequenceNotifier(DeliveryStatus.Sent, DeliveryStatus.Sent);
        var engine = CreateEngine(dbContext, notifier);

        var sentCount = await engine.DispatchAsync([seeded.Event]);
        await dbContext.SaveChangesAsync();

        Assert.Equal(2, sentCount);
        Assert.Equal(2, notifier.CallCount);
        Assert.Equal(2, await dbContext.Deliveries.CountAsync());
    }

    [Fact]
    public async Task DispatchAsync_InstitutionScopeSubscription_ShouldSkipWhenInstitutionRegionDiffers()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedDispatchScopeAsync(dbContext, addInstitutionSubscription: true, institutionRegionKey: "pt-centro");

        var notifier = new SequenceNotifier(DeliveryStatus.Sent, DeliveryStatus.Sent);
        var engine = CreateEngine(dbContext, notifier);

        var sentCount = await engine.DispatchAsync([seeded.Event]);
        await dbContext.SaveChangesAsync();

        Assert.Equal(1, sentCount);
        Assert.Equal(1, notifier.CallCount);

        var delivery = await dbContext.Deliveries.SingleAsync();
        Assert.Equal(seeded.RegionSubscriptionId, delivery.SubscriptionId);
    }

    [Fact]
    public async Task DispatchAsync_WildcardMetricSubscription_ShouldMatchAnyMetric()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedDispatchScopeAsync(
            dbContext,
            addInstitutionSubscription: false,
            regionMetricFilter: "*");

        var notifier = new SequenceNotifier(DeliveryStatus.Sent);
        var engine = CreateEngine(dbContext, notifier);

        var sentCount = await engine.DispatchAsync([seeded.Event]);
        await dbContext.SaveChangesAsync();

        Assert.Equal(1, sentCount);
        Assert.Equal(1, notifier.CallCount);
    }

    [Fact]
    public async Task DispatchAsync_SpecificMetricSubscription_ShouldSkipDifferentMetric()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedDispatchScopeAsync(
            dbContext,
            addInstitutionSubscription: false,
            regionMetricFilter: "blood-group-a-plus");

        var notifier = new SequenceNotifier(DeliveryStatus.Sent);
        var engine = CreateEngine(dbContext, notifier);

        var sentCount = await engine.DispatchAsync([seeded.Event]);
        await dbContext.SaveChangesAsync();

        Assert.Equal(0, sentCount);
        Assert.Equal(0, notifier.CallCount);
        Assert.Equal(0, await dbContext.Deliveries.CountAsync());
    }

    [Fact]
    public async Task DispatchAsync_WildcardMetricSubscription_ShouldSendPerMetricEventWhenMultipleMetricsInScope()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedDispatchScopeAsync(
            dbContext,
            addInstitutionSubscription: false,
            regionMetricFilter: "*");

        var secondEvent = await AddEventWithMetricAsync(
            dbContext,
            seeded.SourceId,
            seeded.RegionId,
            "blood-group-a-plus");

        var notifier = new SequenceNotifier(DeliveryStatus.Sent, DeliveryStatus.Sent);
        var engine = CreateEngine(dbContext, notifier);

        var sentCount = await engine.DispatchAsync([seeded.Event, secondEvent]);
        await dbContext.SaveChangesAsync();

        Assert.Equal(2, sentCount);
        Assert.Equal(2, notifier.CallCount);

        var deliveries = await dbContext.Deliveries.ToListAsync();
        Assert.Equal(2, deliveries.Count);
        Assert.All(deliveries, delivery => Assert.Equal(seeded.RegionSubscriptionId, delivery.SubscriptionId));
        Assert.Equal(2, deliveries.Select(delivery => delivery.EventId).Distinct().Count());
    }

    [Fact]
    public async Task DispatchAsync_InstitutionScopeWildcardMetric_ShouldMatchAnyMetricInInstitutionRegion()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedDispatchScopeAsync(
            dbContext,
            addInstitutionSubscription: true,
            regionMetricFilter: "blood-group-ab-minus",
            institutionMetricFilter: "*");

        var secondEvent = await AddEventWithMetricAsync(
            dbContext,
            seeded.SourceId,
            seeded.RegionId,
            "blood-group-a-plus");

        var notifier = new SequenceNotifier(DeliveryStatus.Sent, DeliveryStatus.Sent);
        var engine = CreateEngine(dbContext, notifier);

        var sentCount = await engine.DispatchAsync([seeded.Event, secondEvent]);
        await dbContext.SaveChangesAsync();

        Assert.Equal(2, sentCount);
        Assert.Equal(2, notifier.CallCount);
        Assert.NotNull(seeded.InstitutionSubscriptionId);

        var deliveries = await dbContext.Deliveries.ToListAsync();
        Assert.Equal(2, deliveries.Count);
        Assert.All(deliveries, delivery => Assert.Equal(seeded.InstitutionSubscriptionId!.Value, delivery.SubscriptionId));
    }

    [Fact]
    public async Task DispatchAsync_LegacyStoredType_ShouldDispatchWithCanonicalNotifier()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedDispatchScopeAsync(dbContext, addInstitutionSubscription: false);

        var subscription = await dbContext.Subscriptions.SingleAsync(entry => entry.Id == seeded.RegionSubscriptionId);
        subscription.TypeKey = "discord-webhook";
        await dbContext.SaveChangesAsync();

        var notifier = new SequenceNotifier("discord:webhook", new DeliveryOutcome(DeliveryStatus.Sent));
        var engine = CreateEngine(dbContext, notifier);

        var sentCount = await engine.DispatchAsync([seeded.Event]);
        await dbContext.SaveChangesAsync();

        Assert.Equal(1, sentCount);
        Assert.Equal(1, notifier.CallCount);
    }

    [Fact]
    public async Task DispatchAsync_TransientFailure_ShouldRetryUpToMaxAttempts()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedDispatchScopeAsync(dbContext, addInstitutionSubscription: false);

        var notifier = new SequenceNotifier(
            "discord:webhook",
            new DeliveryOutcome(DeliveryStatus.Failed, DeliveryFailureKind.Transient),
            new DeliveryOutcome(DeliveryStatus.Failed, DeliveryFailureKind.Transient),
            new DeliveryOutcome(DeliveryStatus.Sent));
        var engine = CreateEngine(dbContext, notifier);

        var sentCount = await engine.DispatchAsync([seeded.Event]);
        await dbContext.SaveChangesAsync();

        Assert.Equal(1, sentCount);
        Assert.Equal(3, notifier.CallCount);

        var delivery = await dbContext.Deliveries.SingleAsync();
        Assert.Equal("sent", delivery.Status);
        Assert.Equal(3, delivery.AttemptCount);
    }

    [Fact]
    public async Task DispatchAsync_PermanentFailure_ShouldNotRetry()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedDispatchScopeAsync(
            dbContext,
            addInstitutionSubscription: false,
            regionTypeKey: "telegram-chat");

        var notifier = new SequenceNotifier(
            "telegram:chat",
            new DeliveryOutcome(DeliveryStatus.Failed, DeliveryFailureKind.Permanent));
        var engine = CreateEngine(dbContext, notifier);

        var sentCount = await engine.DispatchAsync([seeded.Event]);
        await dbContext.SaveChangesAsync();

        Assert.Equal(0, sentCount);
        Assert.Equal(1, notifier.CallCount);

        var delivery = await dbContext.Deliveries.SingleAsync();
        Assert.Equal("failed", delivery.Status);
        Assert.Equal(1, delivery.AttemptCount);
    }

    private static DispatchEngine CreateEngine(BloodWatchDbContext dbContext, SequenceNotifier notifier)
    {
        return new DispatchEngine(
            dbContext,
            [notifier],
            NullLogger<DispatchEngine>.Instance);
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
        bool addInstitutionSubscription,
        string institutionRegionKey = "pt-norte",
        string regionMetricFilter = "blood-group-o-minus",
        string institutionMetricFilter = "blood-group-o-minus",
        string regionTypeKey = "discord:webhook",
        string institutionTypeKey = "discord:webhook")
    {
        var source = new SourceEntity
        {
            Id = Guid.NewGuid(),
            AdapterKey = "pt-dador-ipst",
            Name = "Portugal Dador/IPST",
            CreatedAtUtc = DateTime.UtcNow,
        };

        var eventRegion = new RegionEntity
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            Key = "pt-norte",
            DisplayName = "Norte",
            CreatedAtUtc = DateTime.UtcNow,
        };

        var institutionRegion = string.Equals(institutionRegionKey, eventRegion.Key, StringComparison.Ordinal)
            ? eventRegion
            : new RegionEntity
            {
                Id = Guid.NewGuid(),
                SourceId = source.Id,
                Key = institutionRegionKey,
                DisplayName = institutionRegionKey,
                CreatedAtUtc = DateTime.UtcNow,
            };

        var reserve = new CurrentReserveEntity
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            RegionId = eventRegion.Id,
            MetricKey = "blood-group-o-minus",
            StatusKey = "critical",
            StatusLabel = "Critical",
            ReferenceDate = new DateOnly(2026, 2, 20),
            CapturedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

        var @event = new EventEntity
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            CurrentReserveId = reserve.Id,
            RegionId = eventRegion.Id,
            RuleKey = "reserve-status-transition.v1",
            MetricKey = "blood-group-o-minus",
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            PayloadJson = JsonSerializer.Serialize(new
            {
                signal = "status-alert",
                transitionKind = "entered-non-normal",
                currentStatusKey = "critical",
            }),
            CreatedAtUtc = DateTime.UtcNow,
        };

        var regionSubscription = new SubscriptionEntity
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            TypeKey = regionTypeKey,
            Target = "https://discord.com/api/webhooks/123/token",
            ScopeType = "region",
            RegionFilter = "pt-norte",
            InstitutionId = null,
            MetricFilter = regionMetricFilter,
            IsEnabled = true,
            CreatedAtUtc = DateTime.UtcNow,
        };

        dbContext.Sources.Add(source);
        dbContext.Regions.Add(eventRegion);
        if (!ReferenceEquals(institutionRegion, eventRegion))
        {
            dbContext.Regions.Add(institutionRegion);
        }
        dbContext.CurrentReserves.Add(reserve);
        dbContext.Events.Add(@event);
        dbContext.Subscriptions.Add(regionSubscription);

        Guid? institutionSubscriptionId = null;
        if (addInstitutionSubscription)
        {
            var center = new DonationCenterEntity
            {
                Id = Guid.NewGuid(),
                SourceId = source.Id,
                RegionId = institutionRegion.Id,
                ExternalId = "inst-1",
                InstitutionCode = "SP",
                Name = "CST Porto",
                UpdatedAtUtc = DateTime.UtcNow,
            };

            dbContext.DonationCenters.Add(center);

            institutionSubscriptionId = Guid.NewGuid();
            dbContext.Subscriptions.Add(new SubscriptionEntity
            {
                Id = institutionSubscriptionId.Value,
                SourceId = source.Id,
                TypeKey = institutionTypeKey,
                Target = "https://discord.com/api/webhooks/123/token",
                ScopeType = "institution",
                RegionFilter = null,
                InstitutionId = center.Id,
                MetricFilter = institutionMetricFilter,
                IsEnabled = true,
                CreatedAtUtc = DateTime.UtcNow,
            });
        }

        await dbContext.SaveChangesAsync();

        return new SeededDispatchScope(@event, source.Id, eventRegion.Id, regionSubscription.Id, institutionSubscriptionId);
    }

    private static async Task<EventEntity> AddEventWithMetricAsync(
        BloodWatchDbContext dbContext,
        Guid sourceId,
        Guid regionId,
        string metricKey)
    {
        var reserve = new CurrentReserveEntity
        {
            Id = Guid.NewGuid(),
            SourceId = sourceId,
            RegionId = regionId,
            MetricKey = metricKey,
            StatusKey = "warning",
            StatusLabel = "Warning",
            ReferenceDate = new DateOnly(2026, 2, 20),
            CapturedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

        var @event = new EventEntity
        {
            Id = Guid.NewGuid(),
            SourceId = sourceId,
            CurrentReserveId = reserve.Id,
            RegionId = regionId,
            RuleKey = "reserve-status-transition.v1",
            MetricKey = metricKey,
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            PayloadJson = JsonSerializer.Serialize(new
            {
                signal = "status-alert",
                transitionKind = "worsened",
                currentStatusKey = "warning",
            }),
            CreatedAtUtc = DateTime.UtcNow,
        };

        dbContext.CurrentReserves.Add(reserve);
        dbContext.Events.Add(@event);
        await dbContext.SaveChangesAsync();

        return @event;
    }

    private sealed record SeededDispatchScope(
        EventEntity Event,
        Guid SourceId,
        Guid RegionId,
        Guid RegionSubscriptionId,
        Guid? InstitutionSubscriptionId);

    private sealed record DeliveryOutcome(
        DeliveryStatus Status,
        DeliveryFailureKind FailureKind = DeliveryFailureKind.None);

    private sealed class SequenceNotifier : INotifier
    {
        private readonly Queue<DeliveryOutcome> _outcomes;

        public SequenceNotifier(params DeliveryStatus[] statuses)
            : this(
                "discord:webhook",
                statuses
                    .Select(status => new DeliveryOutcome(
                        status,
                        status == DeliveryStatus.Failed ? DeliveryFailureKind.Transient : DeliveryFailureKind.None))
                    .ToArray())
        {
        }

        public SequenceNotifier(string typeKey, params DeliveryOutcome[] outcomes)
        {
            TypeKey = typeKey;
            _outcomes = new Queue<DeliveryOutcome>(outcomes);
        }

        public string TypeKey { get; }
        public int CallCount { get; private set; }

        public Task<Delivery> SendAsync(Event @event, string target, CancellationToken cancellationToken = default)
        {
            CallCount++;
            var outcome = _outcomes.Count > 0
                ? _outcomes.Dequeue()
                : new DeliveryOutcome(DeliveryStatus.Failed, DeliveryFailureKind.Transient);

            return Task.FromResult(new Delivery(
                TypeKey,
                target,
                outcome.Status,
                DateTime.UtcNow,
                LastError: outcome.Status == DeliveryStatus.Failed ? "send failed" : null,
                SentAtUtc: outcome.Status == DeliveryStatus.Sent ? DateTime.UtcNow : null,
                FailureKind: outcome.FailureKind));
        }
    }
}
