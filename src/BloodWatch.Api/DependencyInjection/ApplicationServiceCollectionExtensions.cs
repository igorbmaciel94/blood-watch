using System.Threading.RateLimiting;
using BloodWatch.Api;
using BloodWatch.Api.Options;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi.Models;
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

        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, OpenApiSecurityScheme>(StringComparer.Ordinal);

                document.Components.SecuritySchemes[ApiAuthConstants.ApiKeySecuritySchemeId] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    Name = ApiAuthConstants.ApiKeyHeaderName,
                    In = ParameterLocation.Header,
                    Description = "API key required for subscription write/read-by-id/delete endpoints.",
                };

                return Task.CompletedTask;
            });

            options.AddOperationTransformer((operation, context, _) =>
            {
                var relativePath = context.Description.RelativePath ?? string.Empty;
                if (!relativePath.StartsWith("api/v1/subscriptions", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.CompletedTask;
                }

                operation.Security ??= [];
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = ApiAuthConstants.ApiKeySecuritySchemeId,
                            },
                        }
                    ] = []
                });

                return Task.CompletedTask;
            });
        });
        return services;
    }
}
