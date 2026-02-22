using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BloodWatch.Core.Contracts;
using BloodWatch.Core.Models;

namespace BloodWatch.Worker.Notifiers;

public sealed class TelegramNotifier(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<TelegramNotifier> logger) : INotifier
{
    private const string BotApiBaseUrl = "https://api.telegram.org";
    private const string BotTokenConfigKey = "BLOODWATCH:TELEGRAM_BOT_TOKEN";
    private const string BotTokenEnvVar = "BLOODWATCH__TELEGRAM_BOT_TOKEN";

    public const string DefaultTypeKey = NotificationChannelTypeCatalog.TelegramChat;

    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<TelegramNotifier> _logger = logger;
    private readonly string? _botToken = ResolveBotToken(configuration);

    public string TypeKey => DefaultTypeKey;

    public async Task<Delivery> SendAsync(Event @event, string target, CancellationToken cancellationToken = default)
    {
        var createdAtUtc = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(_botToken))
        {
            return new Delivery(
                TypeKey,
                target,
                DeliveryStatus.Failed,
                createdAtUtc,
                LastError: "Telegram bot token is not configured.",
                SentAtUtc: null,
                FailureKind: DeliveryFailureKind.Permanent);
        }

        var message = NotificationMessageFormatter.Build(@event);

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{BotApiBaseUrl}/bot{_botToken}/sendMessage")
            {
                Content = JsonContent.Create(new TelegramSendMessageRequest(
                    ChatId: target,
                    Text: BuildText(message),
                    DisableWebPagePreview: true)),
            };

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var apiResponse = ParseTelegramResponse(responseBody);

            if (response.IsSuccessStatusCode && apiResponse.Ok)
            {
                return new Delivery(
                    TypeKey,
                    target,
                    DeliveryStatus.Sent,
                    createdAtUtc,
                    LastError: null,
                    SentAtUtc: DateTime.UtcNow);
            }

            var errorMessage = BuildErrorMessage(response, apiResponse.Description);
            var failureKind = ClassifyFailure(response.StatusCode, apiResponse.ErrorCode, apiResponse.Description);

            _logger.LogWarning(
                "Telegram send failed for target {Target}. Status: {StatusCode}. FailureKind: {FailureKind}.",
                MaskTarget(target),
                (int)response.StatusCode,
                failureKind);

            return new Delivery(
                TypeKey,
                target,
                DeliveryStatus.Failed,
                createdAtUtc,
                LastError: errorMessage,
                SentAtUtc: null,
                FailureKind: failureKind);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            return new Delivery(
                TypeKey,
                target,
                DeliveryStatus.Failed,
                createdAtUtc,
                LastError: ex.Message,
                SentAtUtc: null,
                FailureKind: DeliveryFailureKind.Transient);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Telegram send failed for target {Target}.", MaskTarget(target));
            return new Delivery(
                TypeKey,
                target,
                DeliveryStatus.Failed,
                createdAtUtc,
                LastError: ex.Message,
                SentAtUtc: null,
                FailureKind: DeliveryFailureKind.Transient);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram send failed for target {Target}.", MaskTarget(target));
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

    private static string ResolveBotToken(IConfiguration configuration)
    {
        var value = configuration[BotTokenConfigKey] ?? Environment.GetEnvironmentVariable(BotTokenEnvVar);
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static TelegramApiResponse ParseTelegramResponse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new TelegramApiResponse(false, null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            var ok = root.TryGetProperty("ok", out var okProperty)
                     && okProperty.ValueKind is JsonValueKind.True or JsonValueKind.False
                     && okProperty.GetBoolean();

            int? errorCode = null;
            if (root.TryGetProperty("error_code", out var errorCodeProperty)
                && errorCodeProperty.ValueKind == JsonValueKind.Number
                && errorCodeProperty.TryGetInt32(out var parsedCode))
            {
                errorCode = parsedCode;
            }

            string? description = null;
            if (root.TryGetProperty("description", out var descriptionProperty)
                && descriptionProperty.ValueKind == JsonValueKind.String)
            {
                description = descriptionProperty.GetString();
            }

            return new TelegramApiResponse(ok, errorCode, description);
        }
        catch (JsonException)
        {
            return new TelegramApiResponse(false, null, null);
        }
    }

    private static DeliveryFailureKind ClassifyFailure(
        HttpStatusCode statusCode,
        int? errorCode,
        string? description)
    {
        if (statusCode == HttpStatusCode.TooManyRequests || errorCode == 429)
        {
            return DeliveryFailureKind.Transient;
        }

        if ((int)statusCode >= 500 || (errorCode.HasValue && errorCode.Value >= 500))
        {
            return DeliveryFailureKind.Transient;
        }

        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return DeliveryFailureKind.Permanent;
        }

        var normalizedDescription = description?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalizedDescription.Contains("too many requests", StringComparison.Ordinal)
            || normalizedDescription.Contains("timeout", StringComparison.Ordinal))
        {
            return DeliveryFailureKind.Transient;
        }

        if (normalizedDescription.Contains("chat not found", StringComparison.Ordinal)
            || normalizedDescription.Contains("chat_id", StringComparison.Ordinal)
            || normalizedDescription.Contains("bot was blocked", StringComparison.Ordinal)
            || normalizedDescription.Contains("forbidden", StringComparison.Ordinal)
            || normalizedDescription.Contains("unauthorized", StringComparison.Ordinal))
        {
            return DeliveryFailureKind.Permanent;
        }

        if ((int)statusCode >= 400 && (int)statusCode < 500)
        {
            return DeliveryFailureKind.Permanent;
        }

        return DeliveryFailureKind.Transient;
    }

    private static string BuildErrorMessage(HttpResponseMessage response, string? description)
    {
        var reason = response.ReasonPhrase ?? "no reason";
        var detail = string.IsNullOrWhiteSpace(description) ? string.Empty : $" {description.Trim()}";
        return $"Telegram send failed with {(int)response.StatusCode} ({reason}).{detail}".Trim();
    }

    private static string BuildText(FormattedNotificationMessage message)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"BloodWatch: {message.Title}");
        builder.AppendLine(message.Description);
        builder.AppendLine();
        builder.AppendLine($"Change: {message.ChangeSummary}");
        builder.AppendLine($"Blood group: {message.MetricLabel}");
        builder.AppendLine($"Region: {message.RegionLabel}");
        builder.AppendLine($"Source: {message.SourceLabel}");
        builder.AppendLine($"Captured at: {message.CapturedAtLabel}");
        return builder.ToString().TrimEnd();
    }

    private static string MaskTarget(string target)
    {
        var trimmed = string.IsNullOrWhiteSpace(target) ? string.Empty : target.Trim();
        if (trimmed.Length == 0)
        {
            return "***";
        }

        var suffix = trimmed.Length <= 4 ? trimmed : trimmed[^4..];
        return $"***{suffix}";
    }

    private sealed record TelegramSendMessageRequest(
        [property: JsonPropertyName("chat_id")] string ChatId,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("disable_web_page_preview")] bool DisableWebPagePreview);

    private sealed record TelegramApiResponse(
        bool Ok,
        int? ErrorCode,
        string? Description);
}
