namespace BloodWatch.Api.Options;

public sealed class ApiRateLimitOptions
{
    public const string SectionName = "BloodWatch:Api:RateLimiting";
    public const string PublicReadPolicyName = "public-read";

    public int PermitLimitPerMinute { get; set; } = 60;
    public int QueueLimit { get; set; } = 0;
}
