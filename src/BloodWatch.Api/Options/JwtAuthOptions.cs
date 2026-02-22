namespace BloodWatch.Api.Options;

public sealed class JwtAuthOptions
{
    public const string SectionName = "BloodWatch:JwtAuth";

    public bool Enabled { get; set; } = true;

    public string Issuer { get; set; } = "bloodwatch-api";

    public string Audience { get; set; } = "bloodwatch-clients";

    public string? SigningKey { get; set; }

    public int AccessTokenMinutes { get; set; } = 15;

    public string? AdminEmail { get; set; }

    public string? AdminPasswordHash { get; set; }
}
