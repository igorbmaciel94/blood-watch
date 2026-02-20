using System.Text.RegularExpressions;
using BloodWatch.Api;
using BloodWatch.Api.Contracts;
using BloodWatch.Infrastructure.Persistence;
using BloodWatch.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BloodWatch.Api.Endpoints;

public static class SubscriptionEndpoints
{
    private const string DiscordWebhookType = "discord-webhook";

    private static readonly Regex MetricKeyRegex = new(
        "^[a-z0-9]+(?:-[a-z0-9]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex VersionedWebhookPathRegex = new(
        "^/api/v\\d+/webhooks/",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static IEndpointRouteBuilder MapSubscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/subscriptions")
            .WithTags("Subscriptions");

        group.MapGet(string.Empty, GetSubscriptionsAsync)
            .WithName("GetSubscriptions")
            .WithSummary("List subscriptions.")
            .Produces<SubscriptionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .WithOpenApi();

        group.MapPost(string.Empty, CreateSubscriptionAsync)
            .WithName("CreateSubscription")
            .WithSummary("Create a region and metric scoped subscription.")
            .Produces<SubscriptionResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .WithOpenApi();

        group.MapGet("/{id:guid}", GetSubscriptionByIdAsync)
            .WithName("GetSubscriptionById")
            .WithSummary("Get a subscription by identifier.")
            .Produces<SubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .WithOpenApi();

        group.MapDelete("/{id:guid}", DeleteSubscriptionAsync)
            .WithName("DeleteSubscription")
            .WithSummary("Soft-delete a subscription.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .WithOpenApi();

        return app;
    }

    private static async Task<IResult> GetSubscriptionsAsync(
        [AsParameters] GetSubscriptionsQuery query,
        HttpContext httpContext,
        IConfiguration configuration,
        BloodWatchDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var authFailure = EnsureAuthorized(httpContext, configuration);
        if (authFailure is not null)
        {
            return authFailure;
        }

        var sourceKey = NormalizeRequired(query.Source);
        var regionKey = NormalizeRequired(query.Region);
        var metricKey = NormalizeRequired(query.Metric);

        var subscriptionsQuery = dbContext.Subscriptions
            .AsNoTracking()
            .Where(entry => entry.IsEnabled)
            .AsQueryable();

        if (sourceKey is not null)
        {
            subscriptionsQuery = subscriptionsQuery.Where(entry => entry.Source.AdapterKey == sourceKey);
        }

        if (regionKey is not null)
        {
            subscriptionsQuery = subscriptionsQuery.Where(entry => entry.RegionFilter == regionKey);
        }

        if (metricKey is not null)
        {
            subscriptionsQuery = subscriptionsQuery.Where(entry => entry.MetricFilter == metricKey);
        }

        var items = await subscriptionsQuery
            .OrderByDescending(entry => entry.CreatedAtUtc)
            .ThenBy(entry => entry.Id)
            .Select(entry => new SubscriptionResponse(
                entry.Id,
                entry.Source.AdapterKey,
                entry.TypeKey,
                MaskTarget(entry.Target),
                entry.RegionFilter,
                entry.MetricFilter,
                entry.IsEnabled,
                entry.CreatedAtUtc,
                entry.DisabledAtUtc))
            .ToArrayAsync(cancellationToken);

        return TypedResults.Ok(new SubscriptionsResponse(items));
    }

    private static async Task<IResult> CreateSubscriptionAsync(
        CreateSubscriptionRequest request,
        HttpContext httpContext,
        IConfiguration configuration,
        BloodWatchDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var authFailure = EnsureAuthorized(httpContext, configuration);
        if (authFailure is not null)
        {
            return authFailure;
        }

        var sourceKey = NormalizeRequired(request.Source);
        if (sourceKey is null)
        {
            return CreateBadRequestProblem("Field 'source' is required.");
        }

        var typeKey = NormalizeRequired(request.Type);
        if (typeKey is null)
        {
            return CreateBadRequestProblem("Field 'type' is required.");
        }

        if (!string.Equals(typeKey, DiscordWebhookType, StringComparison.OrdinalIgnoreCase))
        {
            return CreateBadRequestProblem($"Field 'type' must be '{DiscordWebhookType}'.");
        }
        typeKey = DiscordWebhookType;

        var regionKey = NormalizeRequired(request.Region);
        if (regionKey is null)
        {
            return CreateBadRequestProblem("Field 'region' is required.");
        }

        var metricKey = NormalizeRequired(request.Metric);
        if (metricKey is null)
        {
            return CreateBadRequestProblem("Field 'metric' is required.");
        }

        if (!MetricKeyRegex.IsMatch(metricKey))
        {
            return CreateBadRequestProblem("Field 'metric' has an invalid format.");
        }

        var target = NormalizeRequired(request.Target);
        if (target is null)
        {
            return CreateBadRequestProblem("Field 'target' is required.");
        }

        if (!IsValidDiscordWebhookUrl(target))
        {
            return CreateBadRequestProblem("Field 'target' must be a valid Discord webhook URL.");
        }

        var source = await dbContext.Sources
            .SingleOrDefaultAsync(entry => entry.AdapterKey == sourceKey, cancellationToken);

        if (source is null)
        {
            return CreateNotFoundProblem($"Source '{sourceKey}' was not found.");
        }

        var regionExists = await dbContext.Regions
            .AnyAsync(region => region.SourceId == source.Id && region.Key == regionKey, cancellationToken);

        if (!regionExists)
        {
            return CreateNotFoundProblem($"Region '{regionKey}' was not found for source '{source.AdapterKey}'.");
        }

        var hasAnyMetrics = await dbContext.CurrentReserves
            .AnyAsync(reserve => reserve.SourceId == source.Id, cancellationToken);

        if (hasAnyMetrics)
        {
            var metricExists = await dbContext.CurrentReserves
                .AnyAsync(reserve => reserve.SourceId == source.Id && reserve.MetricKey == metricKey, cancellationToken);

            if (!metricExists)
            {
                return CreateBadRequestProblem($"Metric '{metricKey}' was not found for source '{source.AdapterKey}'.");
            }
        }

        var entity = new SubscriptionEntity
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            TypeKey = typeKey,
            Target = target,
            RegionFilter = regionKey,
            MetricFilter = metricKey,
            IsEnabled = true,
            CreatedAtUtc = DateTime.UtcNow,
            DisabledAtUtc = null,
        };

        dbContext.Subscriptions.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created(
            $"/api/v1/subscriptions/{entity.Id}",
            new SubscriptionResponse(
                entity.Id,
                source.AdapterKey,
                entity.TypeKey,
                MaskTarget(entity.Target),
                entity.RegionFilter,
                entity.MetricFilter,
                entity.IsEnabled,
                entity.CreatedAtUtc,
                entity.DisabledAtUtc));
    }

    private static async Task<IResult> GetSubscriptionByIdAsync(
        Guid id,
        HttpContext httpContext,
        IConfiguration configuration,
        BloodWatchDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var authFailure = EnsureAuthorized(httpContext, configuration);
        if (authFailure is not null)
        {
            return authFailure;
        }

        var subscription = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(entry => entry.Id == id)
            .Select(entry => new SubscriptionResponse(
                entry.Id,
                entry.Source.AdapterKey,
                entry.TypeKey,
                MaskTarget(entry.Target),
                entry.RegionFilter,
                entry.MetricFilter,
                entry.IsEnabled,
                entry.CreatedAtUtc,
                entry.DisabledAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

        return subscription is null
            ? CreateNotFoundProblem($"Subscription '{id}' was not found.")
            : TypedResults.Ok(subscription);
    }

    private static async Task<IResult> DeleteSubscriptionAsync(
        Guid id,
        HttpContext httpContext,
        IConfiguration configuration,
        BloodWatchDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var authFailure = EnsureAuthorized(httpContext, configuration);
        if (authFailure is not null)
        {
            return authFailure;
        }

        var subscription = await dbContext.Subscriptions
            .SingleOrDefaultAsync(entry => entry.Id == id, cancellationToken);

        if (subscription is null)
        {
            return CreateNotFoundProblem($"Subscription '{id}' was not found.");
        }

        if (subscription.IsEnabled)
        {
            subscription.IsEnabled = false;
            subscription.DisabledAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.NoContent();
    }

    private static IResult? EnsureAuthorized(HttpContext httpContext, IConfiguration configuration)
    {
        var expectedApiKey = NormalizeRequired(
            configuration["BLOODWATCH:API_KEY"]
            ?? Environment.GetEnvironmentVariable("BLOODWATCH__API_KEY"));

        if (expectedApiKey is null)
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Server configuration error",
                Detail = "Subscription endpoints are unavailable because API key is not configured.",
                Type = "https://httpstatuses.com/500",
            });
        }

        if (!httpContext.Request.Headers.TryGetValue(ApiAuthConstants.ApiKeyHeaderName, out var providedValues))
        {
            return CreateUnauthorizedProblem("Missing API key.");
        }

        var providedApiKey = NormalizeRequired(providedValues.ToString());
        if (providedApiKey is null || !string.Equals(providedApiKey, expectedApiKey, StringComparison.Ordinal))
        {
            return CreateUnauthorizedProblem("Invalid API key.");
        }

        return null;
    }

    private static bool IsValidDiscordWebhookUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var host = uri.Host;
        var hostAllowed = string.Equals(host, "discord.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".discord.com", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "discordapp.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".discordapp.com", StringComparison.OrdinalIgnoreCase);

        if (!hostAllowed)
        {
            return false;
        }

        var path = uri.AbsolutePath;
        if (path.StartsWith("/api/webhooks/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return VersionedWebhookPathRegex.IsMatch(path);
    }

    private static string MaskTarget(string target)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri))
        {
            return "***";
        }

        return $"{uri.Scheme}://{uri.Host}/api/webhooks/***";
    }

    private static string? NormalizeRequired(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static IResult CreateBadRequestProblem(string detail)
    {
        return TypedResults.Problem(new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Bad request",
            Detail = detail,
            Type = "https://httpstatuses.com/400",
        });
    }

    private static IResult CreateNotFoundProblem(string detail)
    {
        return TypedResults.Problem(new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Not found",
            Detail = detail,
            Type = "https://httpstatuses.com/404",
        });
    }

    private static IResult CreateUnauthorizedProblem(string detail)
    {
        return TypedResults.Problem(new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Unauthorized",
            Detail = detail,
            Type = "https://httpstatuses.com/401",
        });
    }
}
