namespace BloodWatch.Copilot.Options;

public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; set; } = "http://localhost:11434";

    public string Model { get; set; } = "llama3.1";

    public int TimeoutSeconds { get; set; } = 30;

    public int MaxRetries { get; set; } = 2;
}
