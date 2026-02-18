using System.Threading.RateLimiting;
using BloodWatch.Api.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BloodWatch.Api.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddBloodWatchApi(this IServiceCollection services)
    {
        services.AddOptions<ApiCachingOptions>()
            .BindConfiguration(ApiCachingOptions.SectionName);

        services.AddOptions<ApiRateLimitOptions>()
            .BindConfiguration(ApiRateLimitOptions.SectionName);

        services.AddMemoryCache();
        services.AddProblemDetails();

        services.AddRateLimiter(rateLimiterOptions =>
        {
            rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            rateLimiterOptions.AddPolicy(ApiRateLimitOptions.PublicReadPolicyName, httpContext =>
            {
                var options = httpContext.RequestServices.GetRequiredService<IOptions<ApiRateLimitOptions>>().Value;

                var permitLimitPerMinute = Math.Clamp(options.PermitLimitPerMinute, 1, 10_000);
                var queueLimit = Math.Clamp(options.QueueLimit, 0, 10_000);

                return RateLimitPartition.GetFixedWindowLimiter(ApiRateLimitOptions.PublicReadPolicyName, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimitPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = queueLimit,
                });
            });

            rateLimiterOptions.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString("0");
                }

                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too many requests",
                    Detail = "Rate limit exceeded for public API endpoints. Retry later.",
                    Type = "https://httpstatuses.com/429",
                    Instance = context.HttpContext.Request.Path,
                };

                await context.HttpContext.Response.WriteAsJsonAsync(
                    problem,
                    options: null,
                    contentType: "application/problem+json",
                    cancellationToken: cancellationToken);
            };
        });

        services.AddOpenApi();
        return services;
    }
}
