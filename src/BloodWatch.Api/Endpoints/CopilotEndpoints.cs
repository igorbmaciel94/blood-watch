using System.Security.Cryptography;
using System.Text;
using BloodWatch.Api.Contracts;
using BloodWatch.Api.Copilot;
using BloodWatch.Api.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BloodWatch.Api.Endpoints;

public static class CopilotEndpoints
{
    public static IEndpointRouteBuilder MapCopilotEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/copilot")
            .WithTags("Copilot (Internal)")
            .RequireRateLimiting(ApiAuthConstants.CopilotRateLimitPolicyName);

        group.MapPost("/ask", AskAsync)
            .WithName("AskCopilot")
            .WithSummary("Ask Copilot questions over read-only analytics.")
            .Produces<CopilotAnswerResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithOpenApi();

        group.MapGet("/briefing/daily", GetDailyBriefingAsync)
            .WithName("GetCopilotDailyBriefing")
            .WithSummary("Generate the Copilot daily briefing from last 24h analytics.")
            .Produces<CopilotBriefingResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithOpenApi();

        group.MapGet("/briefing/weekly", GetWeeklyBriefingAsync)
            .WithName("GetCopilotWeeklyBriefing")
            .WithSummary("Generate the Copilot weekly briefing from reference-date analytics.")
            .Produces<CopilotBriefingResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithOpenApi();

        group.MapGet("/status", GetStatusAsync)
            .WithName("GetCopilotStatus")
            .WithSummary("Get Copilot runtime status.")
            .Produces<CopilotFeatureFlagResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithOpenApi();

        group.MapPost("/feature-flag", SetFeatureFlagAsync)
            .WithName("SetCopilotFeatureFlag")
            .WithSummary("Enable or disable Copilot runtime feature flag.")
            .Produces<CopilotFeatureFlagResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithOpenApi();

        return app;
    }

    private static async Task<IResult> AskAsync(
        CopilotAskRequest request,
        HttpContext httpContext,
        IOptions<CopilotOptions> copilotOptions,
        ICopilotService copilotService,
        CancellationToken cancellationToken)
    {
        var authFailure = AuthorizeAdminKey(httpContext, copilotOptions.Value);
        if (authFailure is not null)
        {
            return authFailure;
        }

        var result = await copilotService.AskAsync(request, cancellationToken);
        if (!result.IsSuccess)
        {
            return EndpointResultFactory.Problem(result.Error!);
        }

        return TypedResults.Ok(result.Value);
    }

    private static async Task<IResult> GetDailyBriefingAsync(
        HttpContext httpContext,
        IOptions<CopilotOptions> copilotOptions,
        ICopilotService copilotService,
        CancellationToken cancellationToken)
    {
        var authFailure = AuthorizeAdminKey(httpContext, copilotOptions.Value);
        if (authFailure is not null)
        {
            return authFailure;
        }

        var result = await copilotService.GetDailyBriefingAsync(cancellationToken);
        if (!result.IsSuccess)
        {
            return EndpointResultFactory.Problem(result.Error!);
        }

        return TypedResults.Ok(result.Value);
    }

    private static async Task<IResult> GetWeeklyBriefingAsync(
        HttpContext httpContext,
        IOptions<CopilotOptions> copilotOptions,
        ICopilotService copilotService,
        CancellationToken cancellationToken)
    {
        var authFailure = AuthorizeAdminKey(httpContext, copilotOptions.Value);
        if (authFailure is not null)
        {
            return authFailure;
        }

        var result = await copilotService.GetWeeklyBriefingAsync(cancellationToken);
        if (!result.IsSuccess)
        {
            return EndpointResultFactory.Problem(result.Error!);
        }

        return TypedResults.Ok(result.Value);
    }

    private static IResult GetStatusAsync(
        HttpContext httpContext,
        IOptions<CopilotOptions> copilotOptions,
        ICopilotFeatureFlagState featureFlagState)
    {
        var authFailure = AuthorizeAdminKey(httpContext, copilotOptions.Value);
        if (authFailure is not null)
        {
            return authFailure;
        }

        return TypedResults.Ok(BuildFeatureFlagResponse(copilotOptions.Value, featureFlagState));
    }

    private static IResult SetFeatureFlagAsync(
        CopilotFeatureFlagRequest request,
        HttpContext httpContext,
        IOptions<CopilotOptions> copilotOptions,
        ICopilotFeatureFlagState featureFlagState)
    {
        var authFailure = AuthorizeAdminKey(httpContext, copilotOptions.Value);
        if (authFailure is not null)
        {
            return authFailure;
        }

        featureFlagState.SetEnabled(request.Enabled);
        return TypedResults.Ok(BuildFeatureFlagResponse(copilotOptions.Value, featureFlagState));
    }

    private static IResult? AuthorizeAdminKey(HttpContext httpContext, CopilotOptions options)
    {
        var expectedApiKey = Normalize(options.AdminApiKey);
        if (expectedApiKey is null)
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Service unavailable",
                Detail = "Copilot admin API key is not configured.",
                Type = "https://httpstatuses.com/503",
            });
        }

        if (!httpContext.Request.Headers.TryGetValue(ApiAuthConstants.CopilotApiKeyHeaderName, out var providedHeaderValues))
        {
            return UnauthorizedProblem();
        }

        var providedApiKey = Normalize(providedHeaderValues.ToString());
        if (providedApiKey is null)
        {
            return UnauthorizedProblem();
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expectedApiKey);
        var providedBytes = Encoding.UTF8.GetBytes(providedApiKey);

        var authorized = expectedBytes.Length == providedBytes.Length
                         && CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);

        return authorized ? null : UnauthorizedProblem();
    }

    private static CopilotFeatureFlagResponse BuildFeatureFlagResponse(CopilotOptions options, ICopilotFeatureFlagState featureFlagState)
    {
        return new CopilotFeatureFlagResponse(
            Enabled: featureFlagState.IsEnabled,
            ConfiguredEnabled: options.Enabled,
            UpdatedAtUtc: featureFlagState.UpdatedAtUtc);
    }

    private static IResult UnauthorizedProblem()
    {
        return TypedResults.Problem(new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Unauthorized",
            Detail = "Missing or invalid admin API key.",
            Type = "https://httpstatuses.com/401",
        });
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}
