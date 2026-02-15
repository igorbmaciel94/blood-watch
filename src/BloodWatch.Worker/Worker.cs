using BloodWatch.Core.Contracts;

namespace BloodWatch.Worker;

public sealed class IngestionWorker(
    IEnumerable<IDataSourceAdapter> adapters,
    ILogger<IngestionWorker> logger) : BackgroundService
{
    private readonly IReadOnlyCollection<IDataSourceAdapter> _adapters = adapters.ToArray();
    private readonly ILogger<IngestionWorker> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var adapter in _adapters)
            {
                try
                {
                    var snapshot = await adapter.FetchLatestAsync(stoppingToken);
                    _logger.LogInformation(
                        "Fetched snapshot from {AdapterKey} at {CapturedAtUtc}. Items: {ItemCount}.",
                        adapter.AdapterKey,
                        snapshot.CapturedAtUtc,
                        snapshot.Items.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Snapshot pull failed for adapter {AdapterKey}.", adapter.AdapterKey);
                }
            }

            await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
        }
    }
}
