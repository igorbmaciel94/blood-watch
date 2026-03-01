using BloodWatch.Api.Contracts;
using BloodWatch.Api.Services;

namespace BloodWatch.Api.Copilot;

public interface ICopilotInfrastructureController
{
    Task<ServiceResult<CopilotFeatureFlagResponse>> GetStatusAsync(CancellationToken cancellationToken);

    Task<ServiceResult<CopilotFeatureFlagResponse>> SetEnabledAsync(bool enabled, CancellationToken cancellationToken);
}
