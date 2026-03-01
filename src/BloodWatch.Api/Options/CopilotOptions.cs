namespace BloodWatch.Api.Options;

public sealed class CopilotOptions
{
    public const string SectionName = "BloodWatch:Copilot";

    public bool Enabled { get; set; } = false;

    public string DefaultSource { get; set; } = "pt-dador-ipst";

    public string? AdminApiKey { get; set; }

    public int DefaultAnalyticsWeeks { get; set; } = 8;

    public int DefaultAnalyticsLimit { get; set; } = 20;

    public CopilotRateLimitingOptions RateLimiting { get; set; } = new();

    public CopilotGuardrailsOptions Guardrails { get; set; } = new();
}

public sealed class CopilotRateLimitingOptions
{
    public int PermitLimitPerMinute { get; set; } = 20;

    public int QueueLimit { get; set; } = 0;
}

public sealed class CopilotGuardrailsOptions
{
    public int MaxQuestionLength { get; set; } = 1000;
}
