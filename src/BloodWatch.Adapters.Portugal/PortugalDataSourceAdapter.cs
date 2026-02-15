using BloodWatch.Core.Contracts;
using BloodWatch.Core.Models;

namespace BloodWatch.Adapters.Portugal;

public sealed class PortugalDataSourceAdapter : IDataSourceAdapter
{
    public const string DefaultAdapterKey = "pt-transparencia-sns";

    private static readonly SourceRef Source = new(DefaultAdapterKey, "Portugal SNS Transparency");
    private static readonly RegionRef PortugalRegion = new("pt", "Portugal");

    public string AdapterKey => DefaultAdapterKey;

    public Task<IReadOnlyCollection<RegionRef>> GetAvailableRegionsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<RegionRef> regions = [PortugalRegion];
        return Task.FromResult(regions);
    }

    public Task<Snapshot> FetchLatestAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = new Snapshot(
            Source,
            DateTime.UtcNow,
            DateOnly.FromDateTime(DateTime.UtcNow),
            Items: []);

        return Task.FromResult(snapshot);
    }
}
