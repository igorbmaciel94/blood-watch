using BloodWatch.Core.Models;

namespace BloodWatch.Core.Tests;

public sealed class SnapshotModelTests
{
    [Fact]
    public void Snapshot_ShouldSupportStatusOnlyItems()
    {
        var source = new SourceRef("pt-dador-ipst", "Portugal Dador/IPST");
        var region = new RegionRef("pt-norte", "Norte");
        var metric = new Metric("blood-group-o-minus", "O-");
        var item = new SnapshotItem(metric, region, "warning", "Warning");

        var snapshot = new Snapshot(
            source,
            DateTime.UtcNow,
            DateOnly.FromDateTime(DateTime.UtcNow),
            [item],
            SourceUpdatedAtUtc: DateTime.UtcNow.AddMinutes(-30));

        Assert.Equal("pt-dador-ipst", snapshot.Source.AdapterKey);
        var mapped = Assert.Single(snapshot.Items);
        Assert.Equal("warning", mapped.StatusKey);
        Assert.Null(mapped.Value);
        Assert.Null(mapped.Unit);
    }
}
