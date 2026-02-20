using BloodWatch.Core.Contracts;
using BloodWatch.Core.Models;
using Microsoft.Extensions.Logging;

namespace BloodWatch.Adapters.Portugal;

public sealed class PortugalAdapter(
    IDadorPtClient client,
    PortugalReservasMapper mapper,
    ILogger<PortugalAdapter> logger) : IDataSourceAdapter
{
    public const string DefaultAdapterKey = "pt-dador-ipst";
    public const string DefaultSourceName = "Portugal Dador/IPST";

    private readonly IDadorPtClient _client = client;
    private readonly PortugalReservasMapper _mapper = mapper;
    private readonly ILogger<PortugalAdapter> _logger = logger;

    private static readonly IReadOnlyCollection<RegionRef> Regions =
    [
        new("pt-ipst", "IPST"),
        new("pt-nacional", "Nacional"),
        new("pt-norte", "Norte"),
        new("pt-centro", "Centro"),
        new("pt-lisboa-setubal", "Lisboa e Setubal"),
        new("pt-alentejo", "Alentejo"),
        new("pt-algarve", "Algarve"),
    ];

    public string AdapterKey => DefaultAdapterKey;

    public Task<IReadOnlyCollection<RegionRef>> GetAvailableRegionsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Regions);
    }

    public async Task<Snapshot> FetchLatestAsync(CancellationToken cancellationToken = default)
    {
        using var payload = await _client.GetBloodReservesPayloadAsync(cancellationToken);

        var snapshot = _mapper.Map(payload.RootElement, DateTime.UtcNow);
        if (snapshot.Items.Count == 0)
        {
            _logger.LogWarning(
                "Portugal adapter fetched dador.pt payload but mapped zero items. ReferenceDate: {ReferenceDate}.",
                snapshot.ReferenceDate);
        }

        return snapshot;
    }
}
