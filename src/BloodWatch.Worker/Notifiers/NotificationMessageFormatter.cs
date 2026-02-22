using System.Globalization;
using System.Text.Json;
using BloodWatch.Core.Models;

namespace BloodWatch.Worker.Notifiers;

internal static class NotificationMessageFormatter
{
    public static FormattedNotificationMessage Build(Event @event)
    {
        var payload = ParsePayload(@event.PayloadJson);
        var template = BuildTemplate(payload, @event);

        var regionLabel = @event.Region?.DisplayName ?? @event.Region?.Key ?? "Unknown region";
        var metricLabel = ToFriendlyMetricLabel(@event.Metric.Key);

        var capturedAtLabel = payload.CapturedAtUtc is not null
            ? payload.CapturedAtUtc.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)
            : @event.CreatedAtUtc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

        return new FormattedNotificationMessage(
            Title: template.Title,
            Description: template.Description,
            MetricLabel: metricLabel,
            RegionLabel: regionLabel,
            SourceLabel: @event.Source.Name,
            CapturedAtLabel: capturedAtLabel,
            PreviousStatusLabel: payload.PreviousStatusLabel ?? "Unknown",
            CurrentStatusLabel: payload.CurrentStatusLabel ?? "Unknown",
            ChangeSummary: $"{payload.PreviousStatusLabel ?? "Unknown"} -> {payload.CurrentStatusLabel ?? "Unknown"}",
            Color: template.Color);
    }

    private static NotificationTemplate BuildTemplate(EventPayload payload, Event @event)
    {
        var regionLabel = @event.Region?.DisplayName ?? @event.Region?.Key ?? "the selected region";
        var metricLabel = ToFriendlyMetricLabel(@event.Metric.Key);

        if (string.Equals(payload.Signal, "recovery", StringComparison.OrdinalIgnoreCase))
        {
            return new NotificationTemplate(
                Title: "Reserve status recovered",
                Description: $"{metricLabel} returned to normal in {regionLabel}.",
                Color: 3066993);
        }

        if (string.Equals(payload.TransitionKind, "worsened", StringComparison.OrdinalIgnoreCase))
        {
            return new NotificationTemplate(
                Title: "Reserve status worsened",
                Description: $"{metricLabel} status worsened in {regionLabel}.",
                Color: 15158332);
        }

        if (string.Equals(payload.TransitionKind, "non-normal-presence", StringComparison.OrdinalIgnoreCase))
        {
            return new NotificationTemplate(
                Title: "Reserve status alert",
                Description: $"{metricLabel} is currently in a non-normal status in {regionLabel}.",
                Color: 15158332);
        }

        return new NotificationTemplate(
            Title: "Reserve status alert",
            Description: $"{metricLabel} entered a non-normal status in {regionLabel}.",
            Color: 15158332);
    }

    private static EventPayload ParsePayload(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;

            return new EventPayload(
                Signal: ReadString(root, "signal"),
                TransitionKind: ReadString(root, "transitionKind"),
                PreviousStatusLabel: ReadString(root, "previousStatusLabel"),
                CurrentStatusLabel: ReadString(root, "currentStatusLabel"),
                CapturedAtUtc: ReadDateTime(root, "capturedAtUtc"));
        }
        catch (JsonException)
        {
            return new EventPayload(
                Signal: null,
                TransitionKind: null,
                PreviousStatusLabel: null,
                CurrentStatusLabel: null,
                CapturedAtUtc: null);
        }
    }

    private static string ToFriendlyMetricLabel(string metricKey)
    {
        if (string.IsNullOrWhiteSpace(metricKey))
        {
            return "Unknown";
        }

        var normalized = metricKey.Trim().ToLowerInvariant();

        const string bloodPrefix = "blood-group-";
        if (!normalized.StartsWith(bloodPrefix, StringComparison.Ordinal))
        {
            return metricKey;
        }

        var suffix = normalized[bloodPrefix.Length..]
            .Replace("-minus", "-", StringComparison.Ordinal)
            .Replace("-plus", "+", StringComparison.Ordinal);

        return suffix.ToUpperInvariant();
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static DateTimeOffset? ReadDateTime(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        if (DateTimeOffset.TryParse(property.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private sealed record NotificationTemplate(string Title, string Description, int Color);

    private sealed record EventPayload(
        string? Signal,
        string? TransitionKind,
        string? PreviousStatusLabel,
        string? CurrentStatusLabel,
        DateTimeOffset? CapturedAtUtc);
}

internal sealed record FormattedNotificationMessage(
    string Title,
    string Description,
    string MetricLabel,
    string RegionLabel,
    string SourceLabel,
    string CapturedAtLabel,
    string PreviousStatusLabel,
    string CurrentStatusLabel,
    string ChangeSummary,
    int Color);
