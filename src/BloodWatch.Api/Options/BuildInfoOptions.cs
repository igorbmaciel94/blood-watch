namespace BloodWatch.Api.Options;

public sealed class BuildInfoOptions
{
    public const string SectionName = "BloodWatch:Build";

    public string? Version { get; set; }
    public string? Commit { get; set; }
    public string? Date { get; set; }
}
