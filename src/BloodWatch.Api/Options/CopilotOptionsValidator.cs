using BloodWatch.Copilot.Options;
using Microsoft.Extensions.Options;

namespace BloodWatch.Api.Options;

public sealed class CopilotOptionsValidator(IOptions<OllamaOptions> ollamaOptions) : IValidateOptions<CopilotOptions>
{
    private readonly IOptions<OllamaOptions> _ollamaOptions = ollamaOptions;

    public ValidateOptionsResult Validate(string? name, CopilotOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.AdminApiKey))
        {
            errors.Add("BloodWatch:Copilot:AdminApiKey is required when Copilot is enabled.");
        }
        else if (options.AdminApiKey.Trim().Length < 16)
        {
            errors.Add("BloodWatch:Copilot:AdminApiKey must be at least 16 characters long.");
        }

        if (string.IsNullOrWhiteSpace(options.DefaultSource))
        {
            errors.Add("BloodWatch:Copilot:DefaultSource is required when Copilot is enabled.");
        }

        var operationTimeout = options.Control.OperationTimeoutSeconds;
        if (operationTimeout is < 5 or > 300)
        {
            errors.Add("BloodWatch:Copilot:Control:OperationTimeoutSeconds must be between 5 and 300.");
        }

        var healthProbeTimeout = options.Control.HealthProbeTimeoutSeconds;
        if (healthProbeTimeout is < 2 or > 120)
        {
            errors.Add("BloodWatch:Copilot:Control:HealthProbeTimeoutSeconds must be between 2 and 120.");
        }

        if (options.Control.Enabled)
        {
            if (string.IsNullOrWhiteSpace(options.Control.OllamaContainerName))
            {
                errors.Add("BloodWatch:Copilot:Control:OllamaContainerName is required when control is enabled.");
            }

            if (string.IsNullOrWhiteSpace(options.Control.OllamaModelInitContainerName))
            {
                errors.Add("BloodWatch:Copilot:Control:OllamaModelInitContainerName is required when control is enabled.");
            }
        }

        var ollama = _ollamaOptions.Value;
        if (string.IsNullOrWhiteSpace(ollama.BaseUrl))
        {
            errors.Add("Ollama:BaseUrl is required when Copilot is enabled.");
        }

        if (string.IsNullOrWhiteSpace(ollama.Model))
        {
            errors.Add("Ollama:Model is required when Copilot is enabled.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
