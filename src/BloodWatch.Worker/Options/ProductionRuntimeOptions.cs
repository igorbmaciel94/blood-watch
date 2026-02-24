namespace BloodWatch.Worker.Options;

public sealed class ProductionRuntimeOptions
{
    public string? ConnectionString { get; set; }
    public string? BuildVersion { get; set; }
    public string? BuildCommit { get; set; }
    public string? BuildDate { get; set; }
}
