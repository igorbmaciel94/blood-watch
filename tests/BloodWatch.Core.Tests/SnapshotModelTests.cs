using BloodWatch.Core.Models;

namespace BloodWatch.Core.Tests;

public sealed class SnapshotModelTests
{
    [Fact]
    public void Snapshot_ShouldKeepCanonicalFields()
    {
        var source = new SourceRef("pt-transparencia-sns", "Portugal SNS Transparency");
        var region = new RegionRef("pt", "Portugal");
        var metric = new Metric("inventory", "Inventory", "units");
        var item = new SnapshotItem(metric, region, 10, "units", "normal");

        var snapshot = new Snapshot(source, DateTime.UtcNow, DateOnly.FromDateTime(DateTime.UtcNow), [item]);

        Assert.Equal("pt-transparencia-sns", snapshot.Source.AdapterKey);
        Assert.Single(snapshot.Items);
    }
}
