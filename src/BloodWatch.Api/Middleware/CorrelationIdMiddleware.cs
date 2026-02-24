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
        if (!string.IsNullOrWhiteSpace(rawHeaderValue))
        {
            var candidate = rawHeaderValue.Trim();
            if (candidate.Length <= 128 && candidate.All(static character => character is >= '!' and <= '~'))
            {
                return candidate;
            }
        }

        return Guid.NewGuid().ToString("N");
    }

    private static string NormalizeBuildValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "unknown"
            : value.Trim();
    }
}
