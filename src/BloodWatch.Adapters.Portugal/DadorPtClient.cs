using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BloodWatch.Adapters.Portugal;

public sealed class DadorPtClient(
    HttpClient httpClient,
    IOptions<DadorPtClientOptions> options,
    ILogger<DadorPtClient> logger) : IDadorPtClient
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly DadorPtClientOptions _options = options.Value;
    private readonly ILogger<DadorPtClient> _logger = logger;

    public Task<JsonDocument> GetBloodReservesPayloadAsync(CancellationToken cancellationToken = default)
    {
        return GetJsonDocumentAsync(_options.BloodReservesPath, "blood-reserves", cancellationToken);
    }

    public Task<JsonDocument> GetInstitutionsPayloadAsync(CancellationToken cancellationToken = default)
    {
        return GetJsonDocumentAsync(_options.InstitutionsPath, "institutions", cancellationToken);
    }

    public Task<JsonDocument> GetSessionsPayloadAsync(CancellationToken cancellationToken = default)
    {
        return GetJsonDocumentAsync(_options.SessionsPath, "sessions", cancellationToken);
    }

    private async Task<JsonDocument> GetJsonDocumentAsync(
        string path,
        string endpointName,
        CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(1, _options.MaxRetries + 1);
        var delay = TimeSpan.FromMilliseconds(400);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, path);
                request.Headers.Accept.ParseAdd("application/json");

                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (IsTransientStatusCode(response.StatusCode) && attempt < maxAttempts)
                {
                    _logger.LogWarning(
                        "dador.pt {Endpoint} returned {StatusCode} on attempt {Attempt}/{MaxAttempts}. Retrying in {DelayMs}ms.",
                        endpointName,
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
                    "dador.pt {Endpoint} request attempt {Attempt}/{MaxAttempts} failed. Retrying in {DelayMs}ms.",
                    endpointName,
                    attempt,
                    maxAttempts,
                    delay.TotalMilliseconds);

                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2);
            }
        }

        throw new HttpRequestException($"Failed to fetch payload '{endpointName}' from dador.pt after retry attempts.");
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
