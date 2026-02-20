using Microsoft.Extensions.Options;

namespace BloodWatch.Worker;

public sealed class IngestionWorker(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<FetchPortugalReservesOptions> optionsMonitor,
    ILogger<IngestionWorker> logger) : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly IOptionsMonitor<FetchPortugalReservesOptions> _optionsMonitor = optionsMonitor;
    private readonly ILogger<IngestionWorker> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var fetchPortugalReservesJob = scope.ServiceProvider.GetRequiredService<FetchPortugalReservesJob>();
                var result = await fetchPortugalReservesJob.ExecuteAsync(stoppingToken);

                _logger.LogInformation(
                    "FetchPortugalReserves completed. InsertedCurrentReserves: {InsertedCurrentReserves}; UpdatedCurrentReserves: {UpdatedCurrentReserves}; CarriedForwardCurrentReserves: {CarriedForwardCurrentReserves}; UpsertedInstitutions: {UpsertedInstitutions}; UpsertedSessions: {UpsertedSessions}; PolledAtUtc: {PolledAtUtc}.",
                    result.InsertedCurrentReserves,
                    result.UpdatedCurrentReserves,
                    result.CarriedForwardCurrentReserves,
                    result.UpsertedInstitutions,
                    result.UpsertedSessions,
                    result.PolledAtUtc);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FetchPortugalReserves execution failed.");
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
    }
}
