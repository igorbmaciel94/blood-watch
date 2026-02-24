using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using BloodWatch.Api;
using BloodWatch.Api.Options;
using BloodWatch.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

namespace BloodWatch.Api.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddBloodWatchApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ApiCachingOptions>()
            .BindConfiguration(ApiCachingOptions.SectionName);

        services.AddOptions<ApiRateLimitOptions>()
            .BindConfiguration(ApiRateLimitOptions.SectionName);

        services.AddOptions<JwtAuthOptions>()
            .BindConfiguration(JwtAuthOptions.SectionName);

        services.AddOptions<BuildInfoOptions>()
            .BindConfiguration(BuildInfoOptions.SectionName);

        services.AddOptions<ProductionRuntimeOptions>()
            .Configure<IConfiguration>((options, config) =>
            {
                options.ConnectionString = config.GetConnectionString("BloodWatch")
                                           ?? config["BLOODWATCH_CONNECTION_STRING"];
                options.JwtSigningKey = config["BloodWatch:JwtAuth:SigningKey"];
                options.JwtAdminEmail = config["BloodWatch:JwtAuth:AdminEmail"];
                options.JwtAdminPasswordHash = config["BloodWatch:JwtAuth:AdminPasswordHash"];
                options.BuildVersion = config["BloodWatch:Build:Version"];
                options.BuildCommit = config["BloodWatch:Build:Commit"];
                options.BuildDate = config["BloodWatch:Build:Date"];
            })
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<ProductionRuntimeOptions>, ProductionRuntimeOptionsValidator>();

        services.AddMemoryCache();
        services.AddProblemDetails();

        services.AddScoped<ISubscriptionService, SubscriptionService>();

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
        }).AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<IOptions<JwtAuthOptions>>((
            bearerOptions,
            jwtAuthOptionsAccessor) =>
        {
            var configuredJwtOptions = jwtAuthOptionsAccessor.Value;
            var validSigningKey = TryGetSigningKeyBytes(configuredJwtOptions.SigningKey, out var configuredKey)
                ? configuredKey
                : RandomNumberGenerator.GetBytes(64);

            bearerOptions.MapInboundClaims = false;
            bearerOptions.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(60),
                ValidIssuer = Normalize(configuredJwtOptions.Issuer) ?? "bloodwatch-api",
                ValidAudience = Normalize(configuredJwtOptions.Audience) ?? "bloodwatch-clients",
                IssuerSigningKey = new SymmetricSecurityKey(validSigningKey),
                NameClaimType = "sub",
                RoleClaimType = ApiAuthConstants.RoleClaimType,
            };
        });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(ApiAuthConstants.SubscriptionWritePolicyName, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(ApiAuthConstants.RoleClaimType, ApiAuthConstants.AdminRoleValue);
            });
        });

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

            rateLimiterOptions.AddPolicy(ApiAuthConstants.AuthTokenRateLimitPolicyName, httpContext =>
            {
                var clientKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(clientKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0,
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
                    Detail = "Rate limit exceeded for this endpoint. Retry later.",
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

                document.Components.SecuritySchemes[ApiAuthConstants.BearerSecuritySchemeId] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "JWT bearer token required for subscription endpoints.",
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
                                Id = ApiAuthConstants.BearerSecuritySchemeId,
                            },
                        }
                    ] = []
                });

                return Task.CompletedTask;
            });
        });

        return services;
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static bool TryGetSigningKeyBytes(string? signingKey, out byte[] keyBytes)
    {
        keyBytes = [];

        var normalized = Normalize(signingKey);
        if (normalized is null)
        {
            return false;
        }

        keyBytes = Encoding.UTF8.GetBytes(normalized);
        return keyBytes.Length >= 32;
    }
}
