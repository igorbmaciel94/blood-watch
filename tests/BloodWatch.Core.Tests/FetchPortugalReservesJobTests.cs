using BloodWatch.Adapters.Portugal;
using BloodWatch.Core.Contracts;
using BloodWatch.Core.Models;
using BloodWatch.Infrastructure.Persistence;
using BloodWatch.Worker;
using BloodWatch.Worker.Alerts;
using BloodWatch.Worker.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BloodWatch.Core.Tests;

public sealed class FetchPortugalReservesJobTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldUpsertCurrentReservesAndTrackPollingHeartbeat()
    {
        var dbOptions = new DbContextOptionsBuilder<BloodWatchDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var dbContext = new BloodWatchDbContext(dbOptions);
        await dbContext.Database.EnsureCreatedAsync();

        var adapter = new SequenceAdapter(
            CreateSnapshot(
                new DateTime(2026, 2, 17, 10, 0, 0, DateTimeKind.Utc),
                new DateOnly(2026, 2, 1),
                [new SnapshotItem(new Metric("overall", "Overall", "units"), new RegionRef("pt-norte", "Regiao de Saude Norte"), 100m, "units")]),
            CreateSnapshot(
                new DateTime(2026, 2, 17, 10, 30, 0, DateTimeKind.Utc),
                new DateOnly(2026, 2, 1),
                [new SnapshotItem(new Metric("overall", "Overall", "units"), new RegionRef("pt-norte", "Regiao de Saude Norte"), 120m, "units")]));

        var job = new FetchPortugalReservesJob(
            [adapter],
            [],
            dbContext,
            CreateDispatchEngine(dbContext),
            NullLogger<FetchPortugalReservesJob>.Instance);

        var firstRun = await job.ExecuteAsync();
        var secondRun = await job.ExecuteAsync();

        Assert.Equal(1, firstRun.InsertedCurrentReserves);
        Assert.Equal(0, firstRun.UpdatedCurrentReserves);
        Assert.Equal(0, firstRun.CarriedForwardCurrentReserves);

        Assert.Equal(0, secondRun.InsertedCurrentReserves);
        Assert.Equal(1, secondRun.UpdatedCurrentReserves);
        Assert.Equal(0, secondRun.CarriedForwardCurrentReserves);
        Assert.True(secondRun.PolledAtUtc >= firstRun.PolledAtUtc);

        var source = await dbContext.Sources.SingleAsync();
        Assert.NotNull(source.LastPolledAtUtc);

        var currentReserve = await dbContext.CurrentReserves.SingleAsync();
        Assert.Equal(120m, currentReserve.Value);
        Assert.Equal(1, await dbContext.CurrentReserves.CountAsync());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCarryForwardWhenCombinationIsMissingFromIncomingPayload()
    {
        var dbOptions = new DbContextOptionsBuilder<BloodWatchDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var dbContext = new BloodWatchDbContext(dbOptions);
        await dbContext.Database.EnsureCreatedAsync();

        var region = new RegionRef("pt-norte", "Regiao de Saude Norte");
        var adapter = new SequenceAdapter(
            CreateSnapshot(
                new DateTime(2026, 2, 17, 11, 0, 0, DateTimeKind.Utc),
                new DateOnly(2026, 2, 1),
                [
                    new SnapshotItem(new Metric("overall", "Overall", "units"), region, 100m, "units"),
                    new SnapshotItem(new Metric("blood-group-o-minus", "O-", "units"), region, 40m, "units")
                ]),
            CreateSnapshot(
                new DateTime(2026, 2, 17, 11, 30, 0, DateTimeKind.Utc),
                new DateOnly(2026, 2, 1),
                [new SnapshotItem(new Metric("overall", "Overall", "units"), region, 110m, "units")]));

        var job = new FetchPortugalReservesJob(
            [adapter],
            [],
            dbContext,
            CreateDispatchEngine(dbContext),
            NullLogger<FetchPortugalReservesJob>.Instance);

        var firstRun = await job.ExecuteAsync();
        var secondRun = await job.ExecuteAsync();

        Assert.Equal(2, firstRun.InsertedCurrentReserves);
        Assert.Equal(0, firstRun.UpdatedCurrentReserves);
        Assert.Equal(0, firstRun.CarriedForwardCurrentReserves);

        Assert.Equal(0, secondRun.InsertedCurrentReserves);
        Assert.Equal(1, secondRun.UpdatedCurrentReserves);
        Assert.Equal(1, secondRun.CarriedForwardCurrentReserves);

        var rows = await dbContext.CurrentReserves
            .OrderBy(entry => entry.MetricKey)
            .ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Equal("blood-group-o-minus", rows[0].MetricKey);
        Assert.Equal(40m, rows[0].Value);
        Assert.Equal("overall", rows[1].MetricKey);
        Assert.Equal(110m, rows[1].Value);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldUpdateLastPolledAtUtcEvenWhenValuesAreUnchanged()
    {
        var dbOptions = new DbContextOptionsBuilder<BloodWatchDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var dbContext = new BloodWatchDbContext(dbOptions);
        await dbContext.Database.EnsureCreatedAsync();

        var snapshot = CreateSnapshot(
            new DateTime(2026, 2, 17, 12, 0, 0, DateTimeKind.Utc),
            new DateOnly(2026, 2, 1),
            [new SnapshotItem(new Metric("overall", "Overall", "units"), new RegionRef("pt-norte", "Regiao de Saude Norte"), 100m, "units")]);

        var adapter = new SequenceAdapter(snapshot, snapshot);
        var job = new FetchPortugalReservesJob(
            [adapter],
            [],
            dbContext,
            CreateDispatchEngine(dbContext),
            NullLogger<FetchPortugalReservesJob>.Instance);

        var firstRun = await job.ExecuteAsync();
        await Task.Delay(10);
        var secondRun = await job.ExecuteAsync();

        Assert.True(secondRun.PolledAtUtc > firstRun.PolledAtUtc);

        var source = await dbContext.Sources.SingleAsync();
        Assert.NotNull(source.LastPolledAtUtc);
        Assert.Equal(secondRun.PolledAtUtc, source.LastPolledAtUtc.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPersistEventsIdempotently()
    {
        var dbOptions = new DbContextOptionsBuilder<BloodWatchDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var dbContext = new BloodWatchDbContext(dbOptions);
        await dbContext.Database.EnsureCreatedAsync();

        var snapshot = CreateSnapshot(
            new DateTime(2026, 2, 19, 12, 0, 0, DateTimeKind.Utc),
            new DateOnly(2026, 2, 1),
            [new SnapshotItem(new Metric("overall", "Overall", "units"), new RegionRef("pt-norte", "Regiao de Saude Norte"), 100m, "units")]);

        var adapter = new SequenceAdapter(snapshot, snapshot);
        var constantEvent = new Event(
            RuleKey: "test-rule",
            Source: snapshot.Source,
            Metric: new Metric("overall", "Overall", "units"),
            Region: new RegionRef("pt-norte", "Regiao de Saude Norte"),
            CreatedAtUtc: new DateTime(2026, 2, 19, 12, 5, 0, DateTimeKind.Utc),
            PayloadJson: "{\"transitionKind\":\"entered-warning\"}");

        var job = new FetchPortugalReservesJob(
            [adapter],
            [new ConstantRule(constantEvent)],
            dbContext,
            CreateDispatchEngine(dbContext),
            NullLogger<FetchPortugalReservesJob>.Instance);

        await job.ExecuteAsync();
        await job.ExecuteAsync();

        var eventRows = await dbContext.Events.ToListAsync();
        Assert.Single(eventRows);
    }

    private static Snapshot CreateSnapshot(
        DateTime capturedAtUtc,
        DateOnly referenceDate,
        IReadOnlyCollection<SnapshotItem> items)
    {
        var source = new SourceRef(PortugalAdapter.DefaultAdapterKey, "Portugal SNS Transparency");
        return new Snapshot(source, capturedAtUtc, referenceDate, items);
    }

    private static DispatchEngine CreateDispatchEngine(BloodWatchDbContext dbContext)
    {
        return new DispatchEngine(
            dbContext,
            [],
            NullLogger<DispatchEngine>.Instance,
            Options.Create(new AlertThresholdOptions()));
    }

    private sealed class SequenceAdapter(params Snapshot[] snapshots) : IDataSourceAdapter
    {
        private readonly IReadOnlyList<Snapshot> _snapshots = snapshots;
        private int _index;

        public string AdapterKey => PortugalAdapter.DefaultAdapterKey;

        public Task<IReadOnlyCollection<RegionRef>> GetAvailableRegionsAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyCollection<RegionRef> regions = [new RegionRef("pt-norte", "Regiao de Saude Norte")];
            return Task.FromResult(regions);
        }

        public Task<Snapshot> FetchLatestAsync(CancellationToken cancellationToken = default)
        {
            var snapshot = _snapshots[Math.Min(_index, _snapshots.Count - 1)];
            _index++;
            return Task.FromResult(snapshot);
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
}
