using BloodWatch.Api.Options;
using Microsoft.Extensions.Options;

namespace BloodWatch.Api.Middleware;

public sealed class CorrelationIdMiddleware(
    RequestDelegate next,
    IHostEnvironment hostEnvironment,
    IOptions<BuildInfoOptions> buildInfoOptions)
{
    private const string HeaderName = "X-Correlation-Id";

    private readonly RequestDelegate _next = next;
    private readonly IHostEnvironment _hostEnvironment = hostEnvironment;
    private readonly IOptions<BuildInfoOptions> _buildInfoOptions = buildInfoOptions;

    public async Task InvokeAsync(HttpContext context, ILogger<CorrelationIdMiddleware> logger)
    {
        var correlationId = ResolveCorrelationId(context.Request.Headers[HeaderName]);
        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        var buildInfo = _buildInfoOptions.Value;
        var scopeValues = new Dictionary<string, object?>
        {
            ["service"] = "bloodwatch-api",
            ["env"] = _hostEnvironment.EnvironmentName,
            ["version"] = NormalizeBuildValue(buildInfo.Version),
            ["commit"] = NormalizeBuildValue(buildInfo.Commit),
            ["correlationId"] = correlationId,
            ["jobName"] = "http-request",
        };

        using (logger.BeginScope(scopeValues))
        {
            await _next(context);
        }
    }

    private static string ResolveCorrelationId(string? rawHeaderValue)
    {
        var sanitized = SanitizeForLogging(rawHeaderValue);
        return string.IsNullOrEmpty(sanitized)
            ? Guid.NewGuid().ToString("N")
            : sanitized;
    }

    private static string SanitizeForLogging(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .Trim();

        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        // Keep only printable ASCII characters to avoid control characters affecting log records.
        var filtered = new string(normalized
            .Where(static character => character is >= '!' and <= '~')
            .ToArray());

        if (filtered.Length == 0)
        {
            return string.Empty;
        }

        return filtered.Length <= 128
            ? filtered
            : filtered[..128];
    }

    private static string NormalizeBuildValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "unknown"
            : value.Trim();
    }
}
