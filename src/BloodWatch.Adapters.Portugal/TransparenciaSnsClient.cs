using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BloodWatch.Adapters.Portugal;

public sealed class TransparenciaSnsClient(
    HttpClient httpClient,
    IOptions<TransparenciaSnsClientOptions> options,
    ILogger<TransparenciaSnsClient> logger) : ITransparenciaSnsClient
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly TransparenciaSnsClientOptions _options = options.Value;
    private readonly ILogger<TransparenciaSnsClient> _logger = logger;

    public async Task<JsonDocument> GetReservasPayloadAsync(CancellationToken cancellationToken = default)
    {
        var maxAttempts = Math.Max(1, _options.MaxRetries + 1);
        var delay = TimeSpan.FromMilliseconds(400);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, _options.DownloadPath);
                request.Headers.Accept.ParseAdd("application/json");

                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (IsTransientStatusCode(response.StatusCode) && attempt < maxAttempts)
                {
                    _logger.LogWarning(
                        "Transparencia SNS status {StatusCode} on attempt {Attempt}/{MaxAttempts}. Retrying in {DelayMs}ms.",
                        (int)response.StatusCode,
                        attempt,
                        maxAttempts,
                        delay.TotalMilliseconds);

                    await Task.Delay(delay, cancellationToken);
                    delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2);
                    continue;
                }

                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            }
            catch (Exception ex) when (ShouldRetry(ex, cancellationToken) && attempt < maxAttempts)
            {
                _logger.LogWarning(
                    ex,
                    "Transparencia SNS request attempt {Attempt}/{MaxAttempts} failed. Retrying in {DelayMs}ms.",
                    attempt,
                    maxAttempts,
                    delay.TotalMilliseconds);

                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2);
            }
        }

        throw new HttpRequestException("Failed to fetch reservas payload from Transparencia SNS after retry attempts.");
    }

    private static bool IsTransientStatusCode(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.RequestTimeout
            || statusCode == HttpStatusCode.TooManyRequests
            || (int)statusCode >= 500;
    }

    private static bool ShouldRetry(Exception exception, CancellationToken cancellationToken)
    {
        return exception switch
        {
            HttpRequestException => true,
            TaskCanceledException when !cancellationToken.IsCancellationRequested => true,
            _ => false,
        };
    }
}
