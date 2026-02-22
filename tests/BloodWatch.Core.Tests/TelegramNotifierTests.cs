using System.Net;
using System.Text;
using System.Text.Json;
using BloodWatch.Core.Models;
using BloodWatch.Worker.Notifiers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace BloodWatch.Core.Tests;

public sealed class TelegramNotifierTests
{
    [Fact]
    public async Task SendAsync_Success_ShouldReturnSentAndComposeStatusSummary()
    {
        var handler = new RecordingHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true,\"result\":{\"message_id\":1}}", Encoding.UTF8, "application/json"),
            });
        var client = new HttpClient(handler);
        var notifier = new TelegramNotifier(client, BuildConfiguration("test-token"), NullLogger<TelegramNotifier>.Instance);

        var result = await notifier.SendAsync(CreateEvent(), "-1001234567890");

        Assert.Equal(DeliveryStatus.Sent, result.Status);
        Assert.Equal(DeliveryFailureKind.None, result.FailureKind);
        Assert.Single(handler.Requests);

        var request = handler.Requests.Single();
        Assert.NotNull(request.Uri);
        Assert.Equal("https://api.telegram.org/bottest-token/sendMessage", request.Uri!.ToString());

        using var payload = JsonDocument.Parse(request.BodyJson ?? "{}");
        Assert.Equal("-1001234567890", payload.RootElement.GetProperty("chat_id").GetString());

        var messageText = payload.RootElement.GetProperty("text").GetString();
        Assert.NotNull(messageText);
        Assert.Contains("Change: Warning -> Critical", messageText, StringComparison.Ordinal);
        Assert.Contains("BloodWatch:", messageText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_ChatNotFound_ShouldReturnPermanentFailure()
    {
        var handler = new RecordingHandler(
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    "{\"ok\":false,\"error_code\":400,\"description\":\"Bad Request: chat not found\"}",
                    Encoding.UTF8,
                    "application/json"),
            });
        var client = new HttpClient(handler);
        var notifier = new TelegramNotifier(client, BuildConfiguration("test-token"), NullLogger<TelegramNotifier>.Instance);

        var result = await notifier.SendAsync(CreateEvent(), "-1001234567890");

        Assert.Equal(DeliveryStatus.Failed, result.Status);
        Assert.Equal(DeliveryFailureKind.Permanent, result.FailureKind);
        Assert.Contains("chat not found", result.LastError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendAsync_RateLimited_ShouldReturnTransientFailure()
    {
        var handler = new RecordingHandler(
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent(
                    "{\"ok\":false,\"error_code\":429,\"description\":\"Too Many Requests\"}",
                    Encoding.UTF8,
                    "application/json"),
            });
        var client = new HttpClient(handler);
        var notifier = new TelegramNotifier(client, BuildConfiguration("test-token"), NullLogger<TelegramNotifier>.Instance);

        var result = await notifier.SendAsync(CreateEvent(), "-1001234567890");

        Assert.Equal(DeliveryStatus.Failed, result.Status);
        Assert.Equal(DeliveryFailureKind.Transient, result.FailureKind);
        Assert.Contains("429", result.LastError, StringComparison.OrdinalIgnoreCase);
    }

    private static IConfiguration BuildConfiguration(string token)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BLOODWATCH:TELEGRAM_BOT_TOKEN"] = token,
            })
            .Build();
    }

    private static Event CreateEvent()
    {
        return new Event(
            RuleKey: "reserve-status-transition.v1",
            Source: new SourceRef("pt-dador-ipst", "Portugal Dador/IPST"),
            Metric: new Metric("blood-group-o-minus", "O-"),
            Region: new RegionRef("pt-norte", "Norte"),
            CreatedAtUtc: DateTime.UtcNow,
            PayloadJson: """
                {
                  "signal":"status-alert",
                  "transitionKind":"worsened",
                  "previousStatusLabel":"Warning",
                  "currentStatusLabel":"Critical",
                  "capturedAtUtc":"2026-02-22T12:00:00Z"
                }
                """);
    }

    private sealed class RecordingHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var bodyJson = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new RecordedRequest(request.RequestUri, bodyJson));

            if (_responses.Count == 0)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json"),
                };
            }

            return _responses.Dequeue();
        }
    }

    private sealed record RecordedRequest(Uri? Uri, string? BodyJson);
}
