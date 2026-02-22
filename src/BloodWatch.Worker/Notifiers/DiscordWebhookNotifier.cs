using System.Globalization;
using System.Net.Http.Json;
using BloodWatch.Core.Contracts;
using BloodWatch.Core.Models;

namespace BloodWatch.Worker.Notifiers;

public sealed class DiscordWebhookNotifier(
    HttpClient httpClient,
    ILogger<DiscordWebhookNotifier> logger) : INotifier
{
    public const string DefaultTypeKey = NotificationChannelTypeCatalog.DiscordWebhook;

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
                SentAtUtc: null,
                FailureKind: DeliveryFailureKind.Transient);
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
                SentAtUtc: null,
                FailureKind: DeliveryFailureKind.Transient);
        }
    }

    private static DiscordWebhookPayload BuildWebhookPayload(Event @event)
    {
        var message = NotificationMessageFormatter.Build(@event);

        var fields = new[]
        {
            new DiscordWebhookField("Blood group", message.MetricLabel, true),
            new DiscordWebhookField("Region", message.RegionLabel, false),
            new DiscordWebhookField("Current status", message.CurrentStatusLabel, true),
            new DiscordWebhookField("Previous status", message.PreviousStatusLabel, true),
            new DiscordWebhookField("Change", message.ChangeSummary, false),
            new DiscordWebhookField("Source", message.SourceLabel, false),
            new DiscordWebhookField("Captured at", message.CapturedAtLabel, false),
        };

        var embed = new DiscordWebhookEmbed(
            Title: message.Title,
            Description: message.Description,
            Color: message.Color,
            Fields: fields,
            Timestamp: DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));

        return new DiscordWebhookPayload(
            Content: $"BloodWatch: {message.Title}",
            Embeds: [embed]);
    }

    private static string MaskTarget(string target)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri))
        {
            return "***";
        }

        return $"{uri.Scheme}://{uri.Host}/api/webhooks/***";
    }

    private sealed record DiscordWebhookPayload(string Content, IReadOnlyCollection<DiscordWebhookEmbed> Embeds);

    private sealed record DiscordWebhookEmbed(
        string Title,
        string Description,
        int Color,
        IReadOnlyCollection<DiscordWebhookField> Fields,
        string Timestamp);

    private sealed record DiscordWebhookField(string Name, string Value, bool Inline);
}
