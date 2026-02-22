using BloodWatch.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace BloodWatch.Api.Endpoints;

internal static class EndpointResultFactory
{
    internal static IResult Problem(ServiceError error)
    {
        return TypedResults.Problem(new ProblemDetails
        {
            Status = error.StatusCode,
            Title = error.Title,
            Detail = error.Detail,
            Type = error.Type,
        });
    }
}
