using BloodWatch.Api.Contracts;
using BloodWatch.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace BloodWatch.Api.Endpoints;

public static class SubscriptionEndpoints
{
    public static IEndpointRouteBuilder MapSubscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/subscriptions")
            .WithTags("Subscriptions")
            .RequireAuthorization(ApiAuthConstants.SubscriptionWritePolicyName);

        group.MapGet(string.Empty, GetSubscriptionsAsync)
            .WithName("GetSubscriptions")
            .WithSummary("List subscriptions.")
            .Produces<SubscriptionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .WithOpenApi();

        group.MapPost(string.Empty, CreateSubscriptionAsync)
            .WithName("CreateSubscription")
            .WithSummary("Create a region- or institution-scoped subscription.")
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

        group.MapGet("/{id:guid}/deliveries", GetSubscriptionDeliveriesAsync)
            .WithName("GetSubscriptionDeliveries")
            .WithSummary("Get latest deliveries for a subscription.")
            .Produces<SubscriptionDeliveriesResponse>(StatusCodes.Status200OK)
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
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        var result = await subscriptionService.GetSubscriptionsAsync(query, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value!)
            : EndpointResultFactory.Problem(result.Error!);
    }

    private static async Task<IResult> CreateSubscriptionAsync(
        CreateSubscriptionRequest request,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        var result = await subscriptionService.CreateSubscriptionAsync(request, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/api/v1/subscriptions/{result.Value!.Id}", result.Value!)
            : EndpointResultFactory.Problem(result.Error!);
    }

    private static async Task<IResult> GetSubscriptionByIdAsync(
        Guid id,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        var result = await subscriptionService.GetSubscriptionByIdAsync(id, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value!)
            : EndpointResultFactory.Problem(result.Error!);
    }

    private static async Task<IResult> GetSubscriptionDeliveriesAsync(
        Guid id,
        [AsParameters] SubscriptionDeliveriesQuery query,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        var result = await subscriptionService.GetSubscriptionDeliveriesAsync(id, query.Limit, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value!)
            : EndpointResultFactory.Problem(result.Error!);
    }

    private static async Task<IResult> DeleteSubscriptionAsync(
        Guid id,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        var result = await subscriptionService.DisableSubscriptionAsync(id, cancellationToken);
        return result.IsSuccess
            ? TypedResults.NoContent()
            : EndpointResultFactory.Problem(result.Error!);
    }
}
