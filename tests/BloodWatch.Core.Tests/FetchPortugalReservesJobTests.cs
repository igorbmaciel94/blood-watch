using BloodWatch.Adapters.Portugal;
using BloodWatch.Core.Contracts;
using BloodWatch.Core.Models;
using BloodWatch.Infrastructure.Persistence;
using BloodWatch.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BloodWatch.Core.Tests;

public sealed class FetchPortugalReservesJobTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldSkipDuplicateSnapshots()
    {
        var dbOptions = new DbContextOptionsBuilder<BloodWatchDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var dbContext = new BloodWatchDbContext(dbOptions);
        await dbContext.Database.EnsureCreatedAsync();

        var adapter = new SequenceAdapter(
            CreateSnapshot(new DateTime(2026, 2, 17, 10, 0, 0, DateTimeKind.Utc)),
            CreateSnapshot(new DateTime(2026, 2, 17, 10, 30, 0, DateTimeKind.Utc)));

        var job = new FetchPortugalReservesJob(
            [adapter],
            dbContext,
            NullLogger<FetchPortugalReservesJob>.Instance);

        var firstRun = await job.ExecuteAsync();
        var secondRun = await job.ExecuteAsync();

        Assert.Equal(1, firstRun.InsertedSnapshots);
        Assert.Equal(0, firstRun.SkippedDuplicates);
        Assert.Equal(1, firstRun.InsertedItems);

        Assert.Equal(0, secondRun.InsertedSnapshots);
        Assert.Equal(1, secondRun.SkippedDuplicates);
        Assert.Equal(0, secondRun.InsertedItems);

        Assert.Equal(1, await dbContext.Snapshots.CountAsync());
        Assert.Equal(1, await dbContext.SnapshotItems.CountAsync());
    }

    private static Snapshot CreateSnapshot(DateTime capturedAtUtc)
    {
        var source = new SourceRef(PortugalAdapter.DefaultAdapterKey, "Portugal SNS Transparency");
        var region = new RegionRef("pt-norte", "Regiao de Saude Norte");
        var metric = new Metric("overall", "Overall", "units");

        var item = new SnapshotItem(metric, region, 100m, "units");
        return new Snapshot(source, capturedAtUtc, new DateOnly(2026, 2, 1), [item]);
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
}
