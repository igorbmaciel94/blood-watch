using System.Text.Json;

namespace BloodWatch.Adapters.Portugal;

public interface IDadorPtClient
{
    Task<JsonDocument> GetBloodReservesPayloadAsync(CancellationToken cancellationToken = default);

    Task<JsonDocument> GetInstitutionsPayloadAsync(CancellationToken cancellationToken = default);

    Task<JsonDocument> GetSessionsPayloadAsync(CancellationToken cancellationToken = default);
}
