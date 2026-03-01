using BloodWatch.Copilot.Models;

namespace BloodWatch.Copilot;

public interface ILLMClient
{
    Task<LLMGenerateResult> GenerateAsync(
        string prompt,
        LLMGenerateOptions options,
        CancellationToken cancellationToken = default);
}
