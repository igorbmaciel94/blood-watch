using System.Text.Json;
using BloodWatch.Adapters.Portugal;
using BloodWatch.Core.Contracts;
using BloodWatch.Core.Models;
using BloodWatch.Infrastructure.Persistence;
using BloodWatch.Infrastructure.Persistence.Entities;
using BloodWatch.Worker;
using BloodWatch.Worker.Dispatch;
using BloodWatch.Worker.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BloodWatch.Core.Tests;

public sealed class FetchPortugalReservesJobTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldUpsertCurrentReservesInstitutionsAndSessions()
    {
        var dbOptions = new DbContextOptionsBuilder<BloodWatchDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var dbContext = new BloodWatchDbContext(dbOptions);
        await dbContext.Database.EnsureCreatedAsync();

        var adapter = new SequenceAdapter(
            CreateSnapshot(
                capturedAtUtc: new DateTime(2026, 2, 20, 10, 0, 0, DateTimeKind.Utc),
                sourceUpdatedAtUtc: new DateTime(2026, 2, 20, 9, 0, 0, DateTimeKind.Utc),
                statusKey: "warning"),
            CreateSnapshot(
                capturedAtUtc: new DateTime(2026, 2, 20, 10, 30, 0, DateTimeKind.Utc),
                sourceUpdatedAtUtc: new DateTime(2026, 2, 20, 9, 30, 0, DateTimeKind.Utc),
                statusKey: "critical"));

        var dadorClient = new FakeDadorClient(
            InstitutionsJson: """
            {
              "data": {
                "CentrosColheita": [
                  {
                    "Id": "1",
                    "SiglaInstituicao": "SP",
                    "DesInstituicao": "CST Porto",
                    "DesNuts": "NORTE",
                    "GeoReferencia": "41.165337,-8.604440"
                  }
                ]
              }
            }
            """,
            SessionsJson: """
            {
              "data": {
                "Sessoes": [
                  {
                    "Id": "500",
                    "SiglaInstituicao": "SP",
                    "DesInstituicao": "CST Porto",
                    "DesNuts": "NORTE",
                    "GeoReferencia": "41.165337,-8.604440",
                    "DataBrigada": "20-02-2026",
                    "HoraBrigada": "08:00 19:30",
                    "Estado": "P",
                    "DesTipoSessao": "POSTO FIXO"
                  }
                ]
              }
            }
            """);

        var job = new FetchPortugalReservesJob(
            [adapter],
            dadorClient,
            new DadorInstitutionsMapper(),
            new DadorSessionsMapper(),
            [],
            dbContext,
            CreateDispatchEngine(dbContext),
            NullLogger<FetchPortugalReservesJob>.Instance);

        var firstRun = await job.ExecuteAsync();
        var secondRun = await job.ExecuteAsync();

        Assert.Equal(1, firstRun.InsertedCurrentReserves);
        Assert.Equal(0, firstRun.UpdatedCurrentReserves);
        Assert.Equal(1, firstRun.UpsertedInstitutions);
        Assert.Equal(1, firstRun.UpsertedSessions);

        Assert.Equal(0, secondRun.InsertedCurrentReserves);
        Assert.Equal(1, secondRun.UpdatedCurrentReserves);

        var reserve = await dbContext.CurrentReserves.SingleAsync();
        Assert.Equal("critical", reserve.StatusKey);

        Assert.Equal(1, await dbContext.DonationCenters.CountAsync());
        Assert.Equal(1, await dbContext.CollectionSessions.CountAsync());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldGenerateAlertsEvenWhenSnapshotIsStale()
    {
        var dbOptions = new DbContextOptionsBuilder<BloodWatchDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var dbContext = new BloodWatchDbContext(dbOptions);
        await dbContext.Database.EnsureCreatedAsync();

        var staleUpdatedAtUtc = DateTime.UtcNow.AddHours(-6);

        var snapshot = CreateSnapshot(
            capturedAtUtc: DateTime.UtcNow,
            sourceUpdatedAtUtc: staleUpdatedAtUtc,
            statusKey: "critical");

        var adapter = new SequenceAdapter(snapshot);
        var dadorClient = new FakeDadorClient(
            InstitutionsJson: """{ "data": { "CentrosColheita": [] } }""",
            SessionsJson: """{ "data": { "Sessoes": [] } }""");

        var constantEvent = new Event(
            RuleKey: "test-rule",
            Source: snapshot.Source,
            Metric: new Metric("blood-group-o-minus", "O-"),
            Region: new RegionRef("pt-norte", "Norte"),
            CreatedAtUtc: DateTime.UtcNow,
            PayloadJson: "{\"signal\":\"status-alert\",\"transitionKind\":\"entered-non-normal\",\"currentStatusKey\":\"critical\"}");

        var job = new FetchPortugalReservesJob(
            [adapter],
            dadorClient,
            new DadorInstitutionsMapper(),
            new DadorSessionsMapper(),
            [new ConstantRule(constantEvent)],
            dbContext,
            CreateDispatchEngine(dbContext),
            NullLogger<FetchPortugalReservesJob>.Instance);

        var result = await job.ExecuteAsync();

        Assert.Equal(2, await dbContext.Events.CountAsync());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldDispatchStatusPresenceEventForSubscriptionCreatedAfterFirstNonNormalRun()
    {
        var dbOptions = new DbContextOptionsBuilder<BloodWatchDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var dbContext = new BloodWatchDbContext(dbOptions);
        await dbContext.Database.EnsureCreatedAsync();

        var snapshot = CreateSnapshot(
            capturedAtUtc: new DateTime(2026, 2, 20, 12, 0, 0, DateTimeKind.Utc),
            sourceUpdatedAtUtc: new DateTime(2026, 2, 20, 11, 0, 0, DateTimeKind.Utc),
            statusKey: "warning");

        var adapter = new SequenceAdapter(snapshot, snapshot);
        var dadorClient = new FakeDadorClient(
            InstitutionsJson: """{ "data": { "CentrosColheita": [] } }""",
            SessionsJson: """{ "data": { "Sessoes": [] } }""");

        var notifier = new SequenceNotifier(DeliveryStatus.Sent);
        var job = new FetchPortugalReservesJob(
            [adapter],
            dadorClient,
            new DadorInstitutionsMapper(),
            new DadorSessionsMapper(),
            [new StatusTransitionRule()],
            dbContext,
            CreateDispatchEngine(dbContext, notifier),
            NullLogger<FetchPortugalReservesJob>.Instance);

        await job.ExecuteAsync();

        var source = await dbContext.Sources.SingleAsync();
        dbContext.Subscriptions.Add(new SubscriptionEntity
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            TypeKey = "discord-webhook",
            Target = "https://discord.com/api/webhooks/123/token",
            ScopeType = "region",
            RegionFilter = "pt-norte",
            InstitutionId = null,
            MetricFilter = "*",
            IsEnabled = true,
            CreatedAtUtc = DateTime.UtcNow,
            DisabledAtUtc = null,
        });
        await dbContext.SaveChangesAsync();

        await job.ExecuteAsync();

        Assert.Equal(1, notifier.CallCount);
        Assert.Equal(1, await dbContext.Deliveries.CountAsync());
    }

    private static Snapshot CreateSnapshot(DateTime capturedAtUtc, DateTime sourceUpdatedAtUtc, string statusKey)
    {
        var source = new SourceRef(PortugalAdapter.DefaultAdapterKey, PortugalAdapter.DefaultSourceName);
        return new Snapshot(
            source,
            capturedAtUtc,
            new DateOnly(2026, 2, 20),
            [new SnapshotItem(
                new Metric("blood-group-o-minus", "O-"),
                new RegionRef("pt-norte", "Norte"),
                statusKey,
                ReserveStatusCatalog.GetLabel(statusKey))],
            sourceUpdatedAtUtc);
    }

    private static DispatchEngine CreateDispatchEngine(
        BloodWatchDbContext dbContext,
        params INotifier[] notifiers)
    {
        return new DispatchEngine(
            dbContext,
            notifiers,
            NullLogger<DispatchEngine>.Instance);
    }

    private sealed class SequenceAdapter(params Snapshot[] snapshots) : IDataSourceAdapter
    {
        private readonly IReadOnlyList<Snapshot> _snapshots = snapshots;
        private int _index;

        public string AdapterKey => PortugalAdapter.DefaultAdapterKey;

        public Task<IReadOnlyCollection<RegionRef>> GetAvailableRegionsAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyCollection<RegionRef> regions = [new RegionRef("pt-norte", "Norte")];
            return Task.FromResult(regions);
        }

        public Task<Snapshot> FetchLatestAsync(CancellationToken cancellationToken = default)
        {
            var snapshot = _snapshots[Math.Min(_index, _snapshots.Count - 1)];
            _index++;
            return Task.FromResult(snapshot);
        }
    }

    private sealed class FakeDadorClient(string InstitutionsJson, string SessionsJson) : IDadorPtClient
    {
        public Task<JsonDocument> GetBloodReservesPayloadAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Adapter test double already provides snapshot data.");
        }

        public Task<JsonDocument> GetInstitutionsPayloadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(JsonDocument.Parse(InstitutionsJson));
        }

        public Task<JsonDocument> GetSessionsPayloadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(JsonDocument.Parse(SessionsJson));
        }
    }

    private sealed class ConstantRule(Event @event) : IRule
    {
        private readonly Event _event = @event;

        public string RuleKey => _event.RuleKey;

        public Task<IReadOnlyCollection<Event>> EvaluateAsync(
            Snapshot? previousSnapshot,
            Snapshot currentSnapshot,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyCollection<Event> events = [_event];
            return Task.FromResult(events);
        }
    }

    private sealed class SequenceNotifier(params DeliveryStatus[] statuses) : INotifier
    {
        private readonly Queue<DeliveryStatus> _statuses = new(statuses);

        public string TypeKey => "discord-webhook";
        public int CallCount { get; private set; }

        public Task<Delivery> SendAsync(Event @event, string target, CancellationToken cancellationToken = default)
        {
            CallCount++;
            var status = _statuses.Count > 0 ? _statuses.Dequeue() : DeliveryStatus.Failed;
            var now = DateTime.UtcNow;

            return Task.FromResult(new Delivery(
                TypeKey,
                target,
                status,
                now,
                LastError: status == DeliveryStatus.Failed ? "send failed" : null,
                SentAtUtc: status == DeliveryStatus.Sent ? now : null));
        }
    }
}
