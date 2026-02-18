namespace BloodWatch.Api.Options;

public sealed class ApiCachingOptions
{
    public const string SectionName = "BloodWatch:Api:Caching";

    public int LatestTtlSeconds { get; set; } = 60;
}
