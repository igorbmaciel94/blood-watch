namespace BloodWatch.Adapters.Portugal;

public sealed class DadorPtClientOptions
{
    public const string SectionName = "BloodWatch:Portugal:Dador";

    public string BaseUrl { get; set; } = "https://dador.pt";
    public string BloodReservesPath { get; set; } = "/api/blood-reserves";
    public string InstitutionsPath { get; set; } = "/api/institutions";
    public string SessionsPath { get; set; } = "/api/sessions";
    public int TimeoutSeconds { get; set; } = 20;
    public int MaxRetries { get; set; } = 3;
    public string UserAgent { get; set; } = "BloodWatch/0.1 (+https://github.com/igorbmaciel/blood-watch)";
}
