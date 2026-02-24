namespace BloodWatch.Api.Options;

public sealed class ProductionRuntimeOptions
{
    public string? ConnectionString { get; set; }
    public string? JwtSigningKey { get; set; }
    public string? JwtAdminEmail { get; set; }
    public string? JwtAdminPasswordHash { get; set; }
    public string? BuildVersion { get; set; }
    public string? BuildCommit { get; set; }
    public string? BuildDate { get; set; }
}
