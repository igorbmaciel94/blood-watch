namespace BloodWatch.Copilot.Models;

public sealed record LLMGenerateResult(
    string Text,
    string Model,
    int? PromptTokens = null,
    int? CompletionTokens = null,
    int? TotalTokens = null);
