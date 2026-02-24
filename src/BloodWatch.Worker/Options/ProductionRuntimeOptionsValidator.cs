using Microsoft.Extensions.Options;

namespace BloodWatch.Worker.Options;

public sealed class ProductionRuntimeOptionsValidator(IHostEnvironment hostEnvironment)
    : IValidateOptions<ProductionRuntimeOptions>
{
    private readonly IHostEnvironment _hostEnvironment = hostEnvironment;

    public ValidateOptionsResult Validate(string? name, ProductionRuntimeOptions options)
    {
        if (!_hostEnvironment.IsProduction())
        {
            return ValidateOptionsResult.Success;
        }

        var errors = new List<string>();
        ValidateRequired(options.ConnectionString, "ConnectionStrings__BloodWatch", errors);
        ValidateRequired(options.BuildVersion, "BloodWatch__Build__Version", errors);
        ValidateRequired(options.BuildCommit, "BloodWatch__Build__Commit", errors);
        ValidateRequired(options.BuildDate, "BloodWatch__Build__Date", errors);

        if (!string.IsNullOrWhiteSpace(options.BuildDate)
            && !DateTimeOffset.TryParse(options.BuildDate, out _))
        {
            errors.Add("BloodWatch__Build__Date must be an ISO-8601 timestamp.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    private static void ValidateRequired(string? value, string envVarName, ICollection<string> errors)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        errors.Add($"Missing required environment variable: {envVarName}.");
    }
}
