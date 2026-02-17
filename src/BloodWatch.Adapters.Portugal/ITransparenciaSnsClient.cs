using System.Text.Json;

namespace BloodWatch.Adapters.Portugal;

public interface ITransparenciaSnsClient
{
    Task<JsonDocument> GetReservasPayloadAsync(CancellationToken cancellationToken = default);
}
