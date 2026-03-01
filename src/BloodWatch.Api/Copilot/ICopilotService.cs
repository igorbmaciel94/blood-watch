using BloodWatch.Api.Contracts;
using BloodWatch.Api.Services;

namespace BloodWatch.Api.Copilot;

public interface ICopilotService
{
    Task<ServiceResult<CopilotAnswerResponse>> AskAsync(CopilotAskRequest request, CancellationToken cancellationToken);

    Task<ServiceResult<CopilotBriefingResponse>> GetDailyBriefingAsync(CancellationToken cancellationToken);

    Task<ServiceResult<CopilotBriefingResponse>> GetWeeklyBriefingAsync(CancellationToken cancellationToken);
}
