using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BloodWatch.Copilot.Models;
using BloodWatch.Copilot.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BloodWatch.Copilot.Ollama;

public sealed class OllamaLLMClient(
    HttpClient httpClient,
    IOptionsMonitor<OllamaOptions> optionsMonitor,
    ILogger<OllamaLLMClient> logger) : ILLMClient
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly IOptionsMonitor<OllamaOptions> _optionsMonitor = optionsMonitor;
    private readonly ILogger<OllamaLLMClient> _logger = logger;

    public async Task<LLMGenerateResult> GenerateAsync(
        string prompt,
        LLMGenerateOptions options,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("Prompt is required.", nameof(prompt));
        }

        var config = _optionsMonitor.CurrentValue;
        var baseUrl = Normalize(config.BaseUrl);
        var model = Normalize(config.Model);

        if (baseUrl is null || model is null)
        {
            throw new LLMClientException(
                "Ollama configuration is incomplete. BaseUrl and Model are required.",
                isTransient: false);
        }

        var timeout = TimeSpan.FromSeconds(Math.Clamp(config.TimeoutSeconds, 1, 300));
        var maxRetries = Math.Clamp(config.MaxRetries, 0, 5);

        Exception? lastError = null;
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(timeout);

                var uri = new Uri(new Uri(EnsureTrailingSlash(baseUrl), UriKind.Absolute), "api/generate");
                using var request = new HttpRequestMessage(HttpMethod.Post, uri)
                {
                    Content = JsonContent.Create(new OllamaRequest(
                        Model: model,
                        Prompt: prompt,
                        Stream: false,
                        System: Normalize(options.SystemPrompt),
                        Options: new OllamaRequestOptions(
                            Temperature: options.Temperature,
                            NumPredict: options.MaxTokens))),
                };

                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutCts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                    var message = $"Ollama returned {(int)response.StatusCode} ({response.ReasonPhrase ?? "no reason"}).";

                    if (ShouldRetry(response.StatusCode) && attempt < maxRetries)
                    {
                        _logger.LogWarning(
                            "Ollama attempt {Attempt}/{MaxAttempts} failed with transient status {StatusCode}. Retrying.",
                            attempt + 1,
                            maxRetries + 1,
                            (int)response.StatusCode);

                        await Task.Delay(ResolveRetryDelay(attempt), cancellationToken);
                        continue;
                    }

                    throw new LLMClientException(
                        string.IsNullOrWhiteSpace(body) ? message : $"{message} {body.Trim()}",
                        isTransient: ShouldRetry(response.StatusCode));
                }

                using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeoutCts.Token);
                var root = document.RootElement;

                var text = TryGetString(root, "response");
                var responseModel = TryGetString(root, "model") ?? model;
                var promptTokens = TryGetInt32(root, "prompt_eval_count");
                var completionTokens = TryGetInt32(root, "eval_count");
                var totalTokens = promptTokens.HasValue && completionTokens.HasValue
                    ? promptTokens.Value + completionTokens.Value
                    : (int?)null;

                if (string.IsNullOrWhiteSpace(text))
                {
                    throw new LLMClientException("Ollama response did not include generated text.", isTransient: false);
                }

                return new LLMGenerateResult(
                    Text: text.Trim(),
                    Model: responseModel,
                    PromptTokens: promptTokens,
                    CompletionTokens: completionTokens,
                    TotalTokens: totalTokens);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                lastError = new LLMClientException("Ollama request timed out.", isTransient: true, ex);
            }
            catch (HttpRequestException ex)
            {
                lastError = new LLMClientException("Unable to reach Ollama endpoint.", isTransient: true, ex);
            }
            catch (LLMClientException ex)
            {
                lastError = ex;
                if (!ex.IsTransient)
                {
                    throw;
                }
            }

            if (attempt < maxRetries)
            {
                await Task.Delay(ResolveRetryDelay(attempt), cancellationToken);
            }
        }

        throw lastError switch
        {
            LLMClientException llmException => llmException,
            null => new LLMClientException("Ollama request failed for an unknown reason.", isTransient: true),
            _ => new LLMClientException("Ollama request failed.", isTransient: true, lastError),
        };
    }

    private static TimeSpan ResolveRetryDelay(int attempt)
    {
        return attempt switch
        {
            0 => TimeSpan.FromMilliseconds(200),
            1 => TimeSpan.FromMilliseconds(500),
            _ => TimeSpan.FromSeconds(1),
        };
    }

    private static bool ShouldRetry(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.RequestTimeout
               || statusCode == HttpStatusCode.TooManyRequests
               || (int)statusCode >= 500;
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string EnsureTrailingSlash(string value)
    {
        return value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/";
    }

    private static string? TryGetString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static int? TryGetInt32(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt32(out var value))
        {
            return null;
        }

        return value;
    }

    private sealed record OllamaRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("system")] string? System,
        [property: JsonPropertyName("options")] OllamaRequestOptions? Options);

    private sealed record OllamaRequestOptions(
        [property: JsonPropertyName("temperature")] double? Temperature,
        [property: JsonPropertyName("num_predict")] int? NumPredict);
}
