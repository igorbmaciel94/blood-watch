using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using BloodWatch.Api.Contracts;
using BloodWatch.Api.Options;
using BloodWatch.Api.Services;
using BloodWatch.Copilot.Options;
using Microsoft.Extensions.Options;

namespace BloodWatch.Api.Copilot;

public sealed class DockerCopilotInfrastructureController(
    IOptions<CopilotOptions> copilotOptions,
    IOptions<OllamaOptions> ollamaOptions,
    ILogger<DockerCopilotInfrastructureController> logger) : ICopilotInfrastructureController
{
    private static readonly HttpClient DockerClient = CreateDockerClient();

    private readonly CopilotOptions _copilotOptions = copilotOptions.Value;
    private readonly OllamaOptions _ollamaOptions = ollamaOptions.Value;
    private readonly ILogger<DockerCopilotInfrastructureController> _logger = logger;

    public async Task<ServiceResult<CopilotFeatureFlagResponse>> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            var containerRunning = await IsOllamaContainerRunningAsync(cancellationToken);
            var effectiveEnabled = _copilotOptions.Enabled && containerRunning;
            return ServiceResult<CopilotFeatureFlagResponse>.Success(new CopilotFeatureFlagResponse(
                Enabled: effectiveEnabled,
                ConfiguredEnabled: _copilotOptions.Enabled,
                UpdatedAtUtc: DateTime.UtcNow));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to read Copilot infrastructure status.");
            return ServiceResult<CopilotFeatureFlagResponse>.Failure(
                StatusCodes.Status503ServiceUnavailable,
                "Service unavailable",
                "Unable to inspect Copilot infrastructure status.");
        }
    }

    public async Task<ServiceResult<CopilotFeatureFlagResponse>> SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        if (!_copilotOptions.Control.Enabled)
        {
            return ServiceResult<CopilotFeatureFlagResponse>.Failure(
                StatusCodes.Status503ServiceUnavailable,
                "Service unavailable",
                "Copilot infrastructure control is disabled in configuration.");
        }

        if (!_copilotOptions.Enabled)
        {
            return ServiceResult<CopilotFeatureFlagResponse>.Failure(
                StatusCodes.Status503ServiceUnavailable,
                "Service unavailable",
                "Copilot is disabled in configuration. Enable BloodWatch:Copilot:Enabled first.");
        }

        if (!File.Exists("/var/run/docker.sock"))
        {
            return ServiceResult<CopilotFeatureFlagResponse>.Failure(
                StatusCodes.Status503ServiceUnavailable,
                "Service unavailable",
                "Docker socket is not mounted. Unable to control Ollama container.");
        }

        var timeout = TimeSpan.FromSeconds(Math.Clamp(_copilotOptions.Control.OperationTimeoutSeconds, 5, 300));
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            if (enabled)
            {
                var startResult = await StartContainerAsync(_copilotOptions.Control.OllamaContainerName, timeoutCts.Token);
                if (!startResult)
                {
                    return ServiceResult<CopilotFeatureFlagResponse>.Failure(
                        StatusCodes.Status503ServiceUnavailable,
                        "Service unavailable",
                        "Failed to start Ollama container. Confirm container exists and API can access Docker socket.");
                }

                _ = await StartContainerAsync(_copilotOptions.Control.OllamaModelInitContainerName, timeoutCts.Token);
                await WaitForOllamaReadinessAsync(timeoutCts.Token);
            }
            else
            {
                await StopContainerAsync(_copilotOptions.Control.OllamaContainerName, timeoutCts.Token);
            }

            var status = await GetStatusAsync(timeoutCts.Token);
            if (!status.IsSuccess)
            {
                return status;
            }

            return ServiceResult<CopilotFeatureFlagResponse>.Success(status.Value! with
            {
                UpdatedAtUtc = DateTime.UtcNow,
            });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ServiceResult<CopilotFeatureFlagResponse>.Failure(
                StatusCodes.Status503ServiceUnavailable,
                "Service unavailable",
                "Copilot infrastructure operation timed out.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Copilot infrastructure control failed for target state {Enabled}", enabled);
            return ServiceResult<CopilotFeatureFlagResponse>.Failure(
                StatusCodes.Status503ServiceUnavailable,
                "Service unavailable",
                "Copilot infrastructure operation failed.");
        }
    }

    private async Task<bool> IsOllamaContainerRunningAsync(CancellationToken cancellationToken)
    {
        using var response = await DockerClient.GetAsync(
            $"/v1.43/containers/{Uri.EscapeDataString(_copilotOptions.Control.OllamaContainerName)}/json",
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Failed to inspect Ollama container status. HTTP {StatusCode}",
                (int)response.StatusCode);
            return false;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var running = document.RootElement
            .GetProperty("State")
            .GetProperty("Running")
            .GetBoolean();

        return running;
    }

    private async Task<bool> StartContainerAsync(string containerName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(containerName))
        {
            return false;
        }

        using var response = await DockerClient.PostAsync(
            $"/v1.43/containers/{Uri.EscapeDataString(containerName)}/start",
            content: null,
            cancellationToken);

        if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotModified)
        {
            return true;
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Container {ContainerName} not found during start request.", containerName);
            return false;
        }

        _logger.LogWarning(
            "Failed to start container {ContainerName}. HTTP {StatusCode}",
            containerName,
            (int)response.StatusCode);
        return false;
    }

    private async Task StopContainerAsync(string containerName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(containerName))
        {
            return;
        }

        using var response = await DockerClient.PostAsync(
            $"/v1.43/containers/{Uri.EscapeDataString(containerName)}/stop?t=10",
            content: null,
            cancellationToken);

        if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotModified or HttpStatusCode.NotFound)
        {
            return;
        }

        _logger.LogWarning(
            "Failed to stop container {ContainerName}. HTTP {StatusCode}",
            containerName,
            (int)response.StatusCode);
    }

    private async Task WaitForOllamaReadinessAsync(CancellationToken cancellationToken)
    {
        var probeTimeout = TimeSpan.FromSeconds(Math.Clamp(_copilotOptions.Control.HealthProbeTimeoutSeconds, 2, 120));
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5),
        };

        if (!Uri.TryCreate(_ollamaOptions.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException("Ollama base URL is invalid.");
        }

        var deadlineUtc = DateTime.UtcNow.Add(probeTimeout);
        var tagsUri = new Uri(baseUri, "/api/tags");

        while (DateTime.UtcNow < deadlineUtc)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var response = await client.GetAsync(tagsUri, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // keep polling within timeout window
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        throw new TimeoutException("Ollama did not become ready within the configured probe timeout.");
    }

    private static HttpClient CreateDockerClient()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (context, cancellationToken) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                await socket.ConnectAsync(new UnixDomainSocketEndPoint("/var/run/docker.sock"), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            },
        };

        return new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }
}
