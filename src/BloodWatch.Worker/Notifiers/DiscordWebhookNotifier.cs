using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using BloodWatch.Core.Contracts;
using BloodWatch.Core.Models;

namespace BloodWatch.Worker.Notifiers;

public sealed class DiscordWebhookNotifier(
    HttpClient httpClient,
    ILogger<DiscordWebhookNotifier> logger) : INotifier
{
    public const string DefaultTypeKey = "discord-webhook";

    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<DiscordWebhookNotifier> _logger = logger;

    public string TypeKey => DefaultTypeKey;

    public async Task<Delivery> SendAsync(Event @event, string target, CancellationToken cancellationToken = default)
    {
        var createdAtUtc = DateTime.UtcNow;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, target)
            {
                Content = JsonContent.Create(BuildWebhookPayload(@event))
            };

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new Delivery(
                    TypeKey,
                    target,
                    DeliveryStatus.Sent,
                    createdAtUtc,
                    LastError: null,
                    SentAtUtc: DateTime.UtcNow);
            }

            var errorMessage = $"Discord webhook returned {(int)response.StatusCode} ({response.ReasonPhrase ?? "no reason"}).";

            _logger.LogWarning(
                "Discord webhook send failed for target {Target}. Status: {StatusCode}.",
                MaskTarget(target),
                (int)response.StatusCode);

            return new Delivery(
                TypeKey,
                target,
                DeliveryStatus.Failed,
                createdAtUtc,
                LastError: errorMessage,
                SentAtUtc: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Discord webhook send failed for target {Target}.", MaskTarget(target));

            return new Delivery(
                TypeKey,
                target,
                DeliveryStatus.Failed,
                createdAtUtc,
                LastError: ex.Message,
                SentAtUtc: null);
        }
    }

    private static DiscordWebhookPayload BuildWebhookPayload(Event @event)
    {
        var payload = ParsePayload(@event.PayloadJson);
        var template = BuildTemplate(payload, @event);

        var regionLabel = @event.Region?.DisplayName ?? @event.Region?.Key ?? "Unknown region";
        var metricLabel = ToFriendlyMetricLabel(@event.Metric.Key);
        var currentUnits = payload.CurrentUnits?.ToString("0.##", CultureInfo.InvariantCulture) ?? "?";
        var criticalUnits = payload.CriticalUnits?.ToString("0.##", CultureInfo.InvariantCulture) ?? "?";

        var capturedAtLabel = payload.CapturedAtUtc is not null
            ? payload.CapturedAtUtc.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)
            : @event.CreatedAtUtc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

        var fields = new[]
        {
            new DiscordWebhookField("Blood group", metricLabel, true),
            new DiscordWebhookField("Region", regionLabel, false),
            new DiscordWebhookField("Available units", currentUnits, true),
            new DiscordWebhookField("Critical threshold", criticalUnits, true),
            new DiscordWebhookField("Source", @event.Source.Name, false),
            new DiscordWebhookField("Captured at", capturedAtLabel, false),
        };

        var embed = new DiscordWebhookEmbed(
            Title: template.Title,
            Description: template.Description,
            Color: template.Color,
            Fields: fields,
            Timestamp: DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));

        return new DiscordWebhookPayload(
            Content: $"BloodWatch: {template.Title}",
            Embeds: [embed]);
    }

    private static NotificationTemplate BuildTemplate(EventPayload payload, Event @event)
    {
        var regionLabel = @event.Region?.DisplayName ?? @event.Region?.Key ?? "the selected region";
        var metricLabel = ToFriendlyMetricLabel(@event.Metric.Key);

        return payload.NotificationKind switch
        {
            "critical-alert" => new NotificationTemplate(
                Title: "Critical stock alert",
                Description: $"{metricLabel} stock is below the critical threshold in {regionLabel}.",
                Color: 15158332),
            "critical-reminder" => new NotificationTemplate(
                Title: "Critical stock reminder",
                Description: $"{metricLabel} stock remains below the critical threshold in {regionLabel}.",
                Color: 15158332),
            "critical-worsening" => new NotificationTemplate(
                Title: "Critical stock worsening",
                Description: $"{metricLabel} stock has worsened and remains in a critical state in {regionLabel}.",
                Color: 15158332),
            "recovery" => new NotificationTemplate(
                Title: "Stock recovered",
                Description: $"{metricLabel} stock has recovered above the critical threshold in {regionLabel}.",
                Color: 3066993),
            _ => new NotificationTemplate(
                Title: "Blood stock update",
                Description: $"{metricLabel} stock status changed in {regionLabel}.",
                Color: 3447003),
        };
    }

    private static EventPayload ParsePayload(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;

            return new EventPayload(
                NotificationKind: ReadString(root, "notificationKind") ?? "critical-alert",
                CurrentUnits: ReadDecimal(root, "currentUnits"),
                CriticalUnits: ReadDecimal(root, "criticalUnits"),
                CapturedAtUtc: ReadDateTime(root, "capturedAtUtc"));
        }
        catch (JsonException)
        {
            return new EventPayload(
                NotificationKind: "critical-alert",
                CurrentUnits: null,
                CriticalUnits: null,
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
        if (normalized == "overall")
        {
            return "Overall";
        }

        const string bloodPrefix = "blood-group-";
        if (!normalized.StartsWith(bloodPrefix, StringComparison.Ordinal))
        {
            return metricKey;
        }

        var suffix = normalized[bloodPrefix.Length..];
        suffix = suffix
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

    private static decimal? ReadDecimal(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var numberValue))
        {
            return numberValue;
        }

        if (property.ValueKind == JsonValueKind.String
            && decimal.TryParse(property.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedValue))
        {
            return parsedValue;
        }

        return null;
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

    private static string MaskTarget(string target)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri))
        {
            return "***";
        }

        return $"{uri.Scheme}://{uri.Host}/api/webhooks/***";
    }

    private sealed record NotificationTemplate(string Title, string Description, int Color);

    private sealed record EventPayload(
        string NotificationKind,
        decimal? CurrentUnits,
        decimal? CriticalUnits,
        DateTimeOffset? CapturedAtUtc);

    private sealed record DiscordWebhookPayload(string Content, IReadOnlyCollection<DiscordWebhookEmbed> Embeds);

    private sealed record DiscordWebhookEmbed(
        string Title,
        string Description,
        int Color,
        IReadOnlyCollection<DiscordWebhookField> Fields,
        string Timestamp);

    private sealed record DiscordWebhookField(string Name, string Value, bool Inline);
}
