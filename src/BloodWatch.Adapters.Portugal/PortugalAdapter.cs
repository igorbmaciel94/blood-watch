using BloodWatch.Core.Contracts;
using BloodWatch.Core.Models;
using Microsoft.Extensions.Logging;

namespace BloodWatch.Adapters.Portugal;

public sealed class PortugalAdapter(
    ITransparenciaSnsClient client,
    PortugalReservasMapper mapper,
    ILogger<PortugalAdapter> logger) : IDataSourceAdapter
{
    public const string DefaultAdapterKey = "pt-transparencia-sns";

    private readonly ITransparenciaSnsClient _client = client;
    private readonly PortugalReservasMapper _mapper = mapper;
    private readonly ILogger<PortugalAdapter> _logger = logger;

    private static readonly IReadOnlyCollection<RegionRef> Regions =
    [
        new("pt-norte", "Regiao de Saude Norte"),
        new("pt-centro", "Regiao de Saude Centro"),
        new("pt-lvt", "Regiao de Saude LVT"),
        new("pt-alentejo", "Regiao de Saude Alentejo"),
        new("pt-algarve", "Regiao de Saude Algarve"),
    ];

    public string AdapterKey => DefaultAdapterKey;

    public Task<IReadOnlyCollection<RegionRef>> GetAvailableRegionsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Regions);
    }

    public async Task<Snapshot> FetchLatestAsync(CancellationToken cancellationToken = default)
    {
        using var payload = await _client.GetReservasPayloadAsync(cancellationToken);

        var capturedAtUtc = DateTime.UtcNow;
        var snapshot = _mapper.Map(payload.RootElement, capturedAtUtc);

        if (snapshot.Items.Count == 0)
        {
            _logger.LogWarning(
                "Portugal adapter fetched payload but mapped zero items. ReferenceDate: {ReferenceDate}.",
                snapshot.ReferenceDate);
        }

        return snapshot;
    }
}
