using System.Text.Json;
using BloodWatch.Core.Models;
using BloodWatch.Worker.Rules;

namespace BloodWatch.Core.Tests;

public sealed class StatusTransitionRuleTests
{
    [Fact]
    public async Task EvaluateAsync_WhenNormalToWarning_ShouldEmitStatusAlert()
    {
        var rule = new StatusTransitionRule();
        var previousSnapshot = CreateSnapshot("normal");
        var currentSnapshot = CreateSnapshot("warning");

        var events = await rule.EvaluateAsync(previousSnapshot, currentSnapshot);

        var @event = Assert.Single(events);
        Assert.Equal("status-alert", ReadStringField(@event.PayloadJson, "signal"));
        Assert.Equal("entered-non-normal", ReadStringField(@event.PayloadJson, "transitionKind"));
    }

    [Fact]
    public async Task EvaluateAsync_WhenWarningToCritical_ShouldEmitWorsenedAlert()
    {
        var rule = new StatusTransitionRule();
        var previousSnapshot = CreateSnapshot("warning");
        var currentSnapshot = CreateSnapshot("critical");

        var events = await rule.EvaluateAsync(previousSnapshot, currentSnapshot);

        var @event = Assert.Single(events);
        Assert.Equal("status-alert", ReadStringField(@event.PayloadJson, "signal"));
        Assert.Equal("worsened", ReadStringField(@event.PayloadJson, "transitionKind"));
    }

    [Fact]
    public async Task EvaluateAsync_WhenCriticalToNormal_ShouldEmitRecovery()
    {
        var rule = new StatusTransitionRule();
        var previousSnapshot = CreateSnapshot("critical");
        var currentSnapshot = CreateSnapshot("normal");

        var events = await rule.EvaluateAsync(previousSnapshot, currentSnapshot);

        var @event = Assert.Single(events);
        Assert.Equal("recovery", ReadStringField(@event.PayloadJson, "signal"));
        Assert.Equal("recovered-to-normal", ReadStringField(@event.PayloadJson, "transitionKind"));
    }

    [Fact]
    public async Task EvaluateAsync_WhenStatusUnchanged_ShouldEmitNoEvents()
    {
        var rule = new StatusTransitionRule();
        var previousSnapshot = CreateSnapshot("warning");
        var currentSnapshot = CreateSnapshot("warning");

        var events = await rule.EvaluateAsync(previousSnapshot, currentSnapshot);

        Assert.Empty(events);
    }

    private static Snapshot CreateSnapshot(string statusKey)
    {
        return new Snapshot(
            new SourceRef("pt-dador-ipst", "Portugal Dador/IPST"),
            new DateTime(2026, 2, 19, 12, 0, 0, DateTimeKind.Utc),
            new DateOnly(2026, 2, 16),
            [new SnapshotItem(
                new Metric("blood-group-o-minus", "O-"),
                new RegionRef("pt-norte", "Norte"),
                statusKey,
                ReserveStatusCatalog.GetLabel(statusKey))],
            SourceUpdatedAtUtc: new DateTime(2026, 2, 19, 11, 0, 0, DateTimeKind.Utc));
    }

    private static string? ReadStringField(string payloadJson, string field)
    {
        using var json = JsonDocument.Parse(payloadJson);
        return json.RootElement.TryGetProperty(field, out var property)
            ? property.GetString()
            : null;
    }
}
