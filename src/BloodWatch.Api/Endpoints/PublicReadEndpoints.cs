using BloodWatch.Api.Contracts;
using BloodWatch.Api.Options;
using BloodWatch.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace BloodWatch.Api.Endpoints;

public static class PublicReadEndpoints
{
    public static IEndpointRouteBuilder MapPublicReadEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1")
            .WithTags("Public Read API")
            .RequireRateLimiting(ApiRateLimitOptions.PublicReadPolicyName);

        group.MapGet("/sources", GetSourcesAsync)
            .WithName("GetSources")
            .WithSummary("List available data sources.")
            .Produces<SourcesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .WithOpenApi();

        group.MapGet("/regions", GetRegionsAsync)
            .WithName("GetRegions")
            .WithSummary("List regions for a source adapter key.")
            .Produces<RegionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .WithOpenApi();

        group.MapGet("/reserves/latest", GetLatestReservesAsync)
            .WithName("GetLatestReserves")
            .WithSummary("Read latest reserves for a source with optional region and metric filters.")
            .Produces<LatestReservesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .WithOpenApi();

        return app;
    }

    private static async Task<IResult> GetSourcesAsync(BloodWatchDbContext dbContext, CancellationToken cancellationToken)
    {
        var items = await dbContext.Sources
            .AsNoTracking()
            .OrderBy(source => source.Name)
            .ThenBy(source => source.AdapterKey)
            .Select(source => new SourceItem(source.AdapterKey, source.Name))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new SourcesResponse(items));
    }

    private static async Task<IResult> GetRegionsAsync(
        [AsParameters] SourceQuery query,
        BloodWatchDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var sourceKey = NormalizeSourceKey(query.Source);
        if (sourceKey is null)
        {
            return CreateBadRequestProblem("Query parameter 'source' is required.");
        }

        var source = await FindSourceAsync(sourceKey, dbContext, cancellationToken);
        if (source is null)
        {
            return CreateNotFoundProblem($"Source '{sourceKey}' was not found.");
        }

        var items = await dbContext.Regions
            .AsNoTracking()
            .Where(region => region.SourceId == source.Id)
            .OrderBy(region => region.DisplayName)
            .ThenBy(region => region.Key)
            .Select(region => new RegionItem(region.Key, region.DisplayName))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new RegionsResponse(source.AdapterKey, items));
    }

    private static async Task<IResult> GetLatestReservesAsync(
        [AsParameters] LatestReservesQuery query,
        BloodWatchDbContext dbContext,
        IMemoryCache memoryCache,
        IOptions<ApiCachingOptions> cachingOptions,
        CancellationToken cancellationToken)
    {
        var sourceKey = NormalizeSourceKey(query.Source);
        if (sourceKey is null)
        {
            return CreateBadRequestProblem("Query parameter 'source' is required.");
        }

        var regionKey = NormalizeRegionKey(query.Region);
        var metricKey = NormalizeMetricKey(query.Metric);

        var cacheKey = $"latest:{sourceKey}|region:{regionKey ?? "*"}|metric:{metricKey ?? "*"}";
        if (memoryCache.TryGetValue(cacheKey, out LatestReservesResponse? cachedResponse) && cachedResponse is not null)
        {
            return TypedResults.Ok(cachedResponse);
        }

        var source = await FindSourceAsync(sourceKey, dbContext, cancellationToken);
        if (source is null)
        {
            return CreateNotFoundProblem($"Source '{sourceKey}' was not found.");
        }

        if (regionKey is not null)
        {
            var regionExists = await dbContext.Regions
                .AsNoTracking()
                .AnyAsync(region => region.SourceId == source.Id && region.Key == regionKey, cancellationToken);
            if (!regionExists)
            {
                return CreateNotFoundProblem($"Region '{regionKey}' was not found for source '{source.AdapterKey}'.");
            }
        }

        if (metricKey is not null)
        {
            var metricExists = await dbContext.CurrentReserves
                .AsNoTracking()
                .AnyAsync(reserve => reserve.SourceId == source.Id && reserve.MetricKey == metricKey, cancellationToken);
            if (!metricExists)
            {
                return CreateNotFoundProblem($"Metric '{metricKey}' was not found for source '{source.AdapterKey}'.");
            }
        }

        var reserveQuery =
            from reserve in dbContext.CurrentReserves.AsNoTracking()
            join region in dbContext.Regions.AsNoTracking() on reserve.RegionId equals region.Id
            where reserve.SourceId == source.Id
            select new { Reserve = reserve, Region = region };

        if (regionKey is not null)
        {
            reserveQuery = reserveQuery.Where(row => row.Region.Key == regionKey);
        }

        if (metricKey is not null)
        {
            reserveQuery = reserveQuery.Where(row => row.Reserve.MetricKey == metricKey);
        }

        var reserveRows = await reserveQuery
            .OrderBy(row => row.Region.Key)
            .ThenBy(row => row.Reserve.MetricKey)
            .Select(row => new CurrentReserveProjection(
                row.Region.Key,
                row.Region.DisplayName,
                row.Reserve.MetricKey,
                row.Reserve.Value,
                row.Reserve.Unit,
                row.Reserve.Severity,
                row.Reserve.CapturedAtUtc,
                row.Reserve.ReferenceDate))
            .ToListAsync(cancellationToken);

        if (reserveRows.Count == 0)
        {
            if (regionKey is null && metricKey is null)
            {
                return CreateNotFoundProblem($"Source '{source.AdapterKey}' has no current reserves.");
            }

            return CreateNotFoundProblem(
                $"No current reserves match the provided filters for source '{source.AdapterKey}'.");
        }

        var latestCapturedAtUtc = reserveRows.Max(row => row.CapturedAtUtc);
        var referenceDate = reserveRows
            .Where(row => row.CapturedAtUtc == latestCapturedAtUtc)
            .Select(row => row.ReferenceDate)
            .OrderByDescending(value => value)
            .FirstOrDefault();

        var items = reserveRows
            .Select(row => new LatestReservesItem(
                new RegionItem(row.RegionKey, row.RegionDisplayName),
                row.MetricKey,
                row.Value,
                row.Unit,
                row.Severity))
            .ToArray();

        var response = new LatestReservesResponse(
            source.AdapterKey,
            latestCapturedAtUtc,
            referenceDate,
            items);

        var ttlSeconds = Math.Clamp(cachingOptions.Value.LatestTtlSeconds, 1, 86_400);
        memoryCache.Set(cacheKey, response, TimeSpan.FromSeconds(ttlSeconds));

        return TypedResults.Ok(response);
    }

    private static async Task<SourceProjection?> FindSourceAsync(
        string sourceKey,
        BloodWatchDbContext dbContext,
        CancellationToken cancellationToken)
    {
        return await dbContext.Sources
            .AsNoTracking()
            .Where(source => source.AdapterKey == sourceKey)
            .Select(source => new SourceProjection(source.Id, source.AdapterKey))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static string? NormalizeSourceKey(string? source)
    {
        return NormalizeRequired(source);
    }

    private static string? NormalizeRegionKey(string? region)
    {
        return NormalizeRequired(region);
    }

    private static string? NormalizeMetricKey(string? metric)
    {
        return NormalizeRequired(metric);
    }

    private static string? NormalizeRequired(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
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

    private sealed record SourceProjection(Guid Id, string AdapterKey);

    private sealed record CurrentReserveProjection(
        string RegionKey,
        string RegionDisplayName,
        string MetricKey,
        decimal Value,
        string Unit,
        string? Severity,
        DateTime CapturedAtUtc,
        DateOnly? ReferenceDate);
}
