namespace BloodWatch.Copilot.Models;

public sealed record LLMGenerateOptions(
    string? SystemPrompt = null,
    double? Temperature = null,
    int? MaxTokens = null);
