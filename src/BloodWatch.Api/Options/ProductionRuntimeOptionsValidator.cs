using Microsoft.Extensions.Options;

namespace BloodWatch.Api.Options;

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
        ValidateRequired(options.JwtAdminEmail, "BloodWatch__JwtAuth__AdminEmail", errors);
        ValidateRequired(options.JwtAdminPasswordHash, "BloodWatch__JwtAuth__AdminPasswordHash", errors);
        ValidateRequired(options.BuildVersion, "BloodWatch__Build__Version", errors);
        ValidateRequired(options.BuildCommit, "BloodWatch__Build__Commit", errors);
        ValidateRequired(options.BuildDate, "BloodWatch__Build__Date", errors);

        if (string.IsNullOrWhiteSpace(options.JwtSigningKey))
        {
            errors.Add("Missing required environment variable: BloodWatch__JwtAuth__SigningKey.");
        }
        else if (options.JwtSigningKey.Trim().Length < 32)
        {
            errors.Add("BloodWatch__JwtAuth__SigningKey must be at least 32 characters long.");
        }

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
