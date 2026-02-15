using BloodWatch.Core.Models;

namespace BloodWatch.Core.Contracts;

public interface IDataSourceAdapter
{
    string AdapterKey { get; }

    Task<IReadOnlyCollection<RegionRef>> GetAvailableRegionsAsync(CancellationToken cancellationToken = default);

    Task<Snapshot> FetchLatestAsync(CancellationToken cancellationToken = default);
}
