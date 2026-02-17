namespace BloodWatch.Adapters.Portugal;

public sealed class TransparenciaSnsClientOptions
{
    public const string SectionName = "BloodWatch:Portugal:TransparenciaSns";

    public string BaseUrl { get; set; } = "https://transparencia.sns.gov.pt";
    public string DownloadPath { get; set; } = "/explore/dataset/reservas/download?format=json&timezone=UTC&use_labels_for_header=false";
    public int TimeoutSeconds { get; set; } = 20;
    public int MaxRetries { get; set; } = 3;
    public string UserAgent { get; set; } = "BloodWatch/0.1 (+https://github.com/igorbmaciel/blood-watch)";
}
