using System.Diagnostics;
using BloodWatch.Worker.Options;
using Microsoft.Extensions.Options;

namespace BloodWatch.Worker;

public sealed class IngestionWorker(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<FetchPortugalReservesOptions> optionsMonitor,
    IHostEnvironment hostEnvironment,
    IOptions<BuildInfoOptions> buildInfoOptions,
    ILogger<IngestionWorker> logger) : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly IOptionsMonitor<FetchPortugalReservesOptions> _optionsMonitor = optionsMonitor;
    private readonly IHostEnvironment _hostEnvironment = hostEnvironment;
    private readonly BuildInfoOptions _buildInfo = buildInfoOptions.Value;
    private readonly ILogger<IngestionWorker> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Ingestion worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var correlationId = Guid.NewGuid().ToString("N");
            using var loggingScope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["service"] = "bloodwatch-worker",
                ["env"] = _hostEnvironment.EnvironmentName,
                ["version"] = NormalizeBuildValue(_buildInfo.Version),
                ["commit"] = NormalizeBuildValue(_buildInfo.Commit),
                ["correlationId"] = correlationId,
                ["jobName"] = "ingest-rules-dispatch",
            });

            var cycleStopwatch = Stopwatch.StartNew();

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var fetchPortugalReservesJob = scope.ServiceProvider.GetRequiredService<FetchPortugalReservesJob>();
                var result = await fetchPortugalReservesJob.ExecuteAsync(stoppingToken);
                cycleStopwatch.Stop();

                _logger.LogInformation(
                    "FetchPortugalReserves cycle completed in {CycleDurationMs}ms. InsertedCurrentReserves: {InsertedCurrentReserves}; UpdatedCurrentReserves: {UpdatedCurrentReserves}; CarriedForwardCurrentReserves: {CarriedForwardCurrentReserves}; UpsertedInstitutions: {UpsertedInstitutions}; UpsertedSessions: {UpsertedSessions}; GeneratedEvents: {GeneratedEvents}; DispatchCandidates: {DispatchCandidates}; SentDeliveries: {SentDeliveries}; IngestDurationMs: {IngestDurationMs}; RulesDurationMs: {RulesDurationMs}; DispatchDurationMs: {DispatchDurationMs}; PolledAtUtc: {PolledAtUtc}.",
                    cycleStopwatch.ElapsedMilliseconds,
                    result.InsertedCurrentReserves,
                    result.UpdatedCurrentReserves,
                    result.CarriedForwardCurrentReserves,
                    result.UpsertedInstitutions,
                    result.UpsertedSessions,
                    result.GeneratedEvents,
                    result.DispatchCandidates,
                    result.SentDeliveries,
                    result.IngestDurationMs,
                    result.RulesDurationMs,
                    result.DispatchDurationMs,
                    result.PolledAtUtc);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                cycleStopwatch.Stop();
                _logger.LogError(ex, "FetchPortugalReserves execution failed after {CycleDurationMs}ms.", cycleStopwatch.ElapsedMilliseconds);
            }

            try
            {
                await Task.Delay(_optionsMonitor.CurrentValue.GetInterval(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Ingestion worker stopped.");
    }

    private static string NormalizeBuildValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "unknown"
            : value.Trim();
    }
}
