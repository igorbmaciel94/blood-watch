using System.Text.Json;
using BloodWatch.Core.Models;
using BloodWatch.Worker.Alerts;
using BloodWatch.Worker.Rules;
using Microsoft.Extensions.Options;

namespace BloodWatch.Core.Tests;

public sealed class LowStockThresholdRuleTests
{
    [Fact]
    public async Task EvaluateAsync_WhenInitialValueIsCritical_ShouldEmitCriticalActiveSignal()
    {
        var rule = CreateRule();
        var currentSnapshot = CreateSnapshot(value: 90m, metricKey: "overall");

        var events = await rule.EvaluateAsync(previousSnapshot: null, currentSnapshot);

        var @event = Assert.Single(events);
        Assert.Equal("critical-active", ReadStringField(@event.PayloadJson, "signal"));
        Assert.Equal("initial-critical", ReadStringField(@event.PayloadJson, "transitionKind"));
    }

    [Fact]
    public async Task EvaluateAsync_WhenValueStaysCritical_ShouldEmitCriticalActiveSignal()
    {
        var rule = CreateRule();
        var previousSnapshot = CreateSnapshot(value: 90m, metricKey: "overall");
        var currentSnapshot = CreateSnapshot(value: 90m, metricKey: "overall");

        var events = await rule.EvaluateAsync(previousSnapshot, currentSnapshot);

        var @event = Assert.Single(events);
        Assert.Equal("critical-active", ReadStringField(@event.PayloadJson, "signal"));
        Assert.Equal("still-critical", ReadStringField(@event.PayloadJson, "transitionKind"));
    }

    [Fact]
    public async Task EvaluateAsync_WhenNormalToCritical_ShouldEmitEnteredCriticalTransition()
    {
        var rule = CreateRule();
        var previousSnapshot = CreateSnapshot(value: 130m, metricKey: "overall");
        var currentSnapshot = CreateSnapshot(value: 90m, metricKey: "overall");

        var events = await rule.EvaluateAsync(previousSnapshot, currentSnapshot);

        var @event = Assert.Single(events);
        Assert.Equal("critical-active", ReadStringField(@event.PayloadJson, "signal"));
        Assert.Equal("entered-critical", ReadStringField(@event.PayloadJson, "transitionKind"));
    }

    [Fact]
    public async Task EvaluateAsync_WhenRecoveringFromCritical_ShouldEmitRecoverySignal()
    {
        var rule = CreateRule();
        var previousSnapshot = CreateSnapshot(value: 90m, metricKey: "overall");
        var currentSnapshot = CreateSnapshot(value: 130m, metricKey: "overall");

        var events = await rule.EvaluateAsync(previousSnapshot, currentSnapshot);

        var @event = Assert.Single(events);
        Assert.Equal("recovery", ReadStringField(@event.PayloadJson, "signal"));
        Assert.Equal("recovered-from-critical", ReadStringField(@event.PayloadJson, "transitionKind"));
    }

    [Fact]
    public async Task EvaluateAsync_WhenValueRemainsNormal_ShouldEmitNoEvents()
    {
        var rule = CreateRule();
        var previousSnapshot = CreateSnapshot(value: 140m, metricKey: "overall");
        var currentSnapshot = CreateSnapshot(value: 130m, metricKey: "overall");

        var events = await rule.EvaluateAsync(previousSnapshot, currentSnapshot);

        Assert.Empty(events);
    }

    private static LowStockThresholdRule CreateRule()
    {
        var options = Options.Create(new AlertThresholdOptions
        {
            BaseCriticalUnits = 100m,
            WarningMultiplier = 1.2m,
            CriticalStepDownPercent = 0.10m,
        });

        return new LowStockThresholdRule(
            new AlertThresholdProfileResolver(
                options,
                new CompatibilityPriorityService()));
    }

    private static Snapshot CreateSnapshot(decimal value, string metricKey)
    {
        return new Snapshot(
            new SourceRef("pt-transparencia-sns", "Portugal SNS Transparency"),
            new DateTime(2026, 2, 19, 12, 0, 0, DateTimeKind.Utc),
            new DateOnly(2026, 2, 1),
            [new SnapshotItem(
                new Metric(metricKey, metricKey, "units"),
                new RegionRef("pt-norte", "Regiao de Saude Norte"),
                value,
                "units")]);
    }

    private static string? ReadStringField(string payloadJson, string field)
    {
        using var json = JsonDocument.Parse(payloadJson);
        return json.RootElement.TryGetProperty(field, out var property)
            ? property.GetString()
            : null;
    }
}
