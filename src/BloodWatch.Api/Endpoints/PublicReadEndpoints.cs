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
            .WithSummary("Read latest reserve statuses for a source with optional region and metric filters.")
            .Produces<LatestReservesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .WithOpenApi();

        group.MapGet("/institutions", GetInstitutionsAsync)
            .WithName("GetInstitutions")
            .WithSummary("List donation institutions.")
            .Produces<InstitutionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .WithOpenApi();

        group.MapGet("/institutions/nearest", GetNearestInstitutionsAsync)
            .WithName("GetNearestInstitutions")
            .WithSummary("List nearest donation institutions by latitude and longitude.")
            .Produces<NearestInstitutionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .WithOpenApi();

        group.MapGet("/sessions", GetSessionsAsync)
            .WithName("GetSessions")
            .WithSummary("List upcoming donation sessions.")
            .Produces<SessionsResponse>(StatusCodes.Status200OK)
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
        var sourceKey = NormalizeRequired(query.Source);
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
        var sourceKey = NormalizeRequired(query.Source);
        if (sourceKey is null)
        {
            return CreateBadRequestProblem("Query parameter 'source' is required.");
        }

        var regionKey = NormalizeRequired(query.Region);
        var metricKey = NormalizeRequired(query.Metric);

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
            select new
            {
                RegionKey = region.Key,
                RegionDisplayName = region.DisplayName,
                reserve.MetricKey,
                reserve.StatusKey,
                reserve.StatusLabel,
                reserve.CapturedAtUtc,
                reserve.ReferenceDate,
            };

        if (regionKey is not null)
        {
            reserveQuery = reserveQuery.Where(row => row.RegionKey == regionKey);
        }

        if (metricKey is not null)
        {
            reserveQuery = reserveQuery.Where(row => row.MetricKey == metricKey);
        }

        var reserveRows = await reserveQuery
            .OrderBy(row => row.RegionKey)
            .ThenBy(row => row.MetricKey)
            .ToListAsync(cancellationToken);

        if (reserveRows.Count == 0)
        {
            if (regionKey is null && metricKey is null)
            {
                return CreateNotFoundProblem($"Source '{source.AdapterKey}' has no current reserves.");
            }

            return CreateNotFoundProblem($"No current reserves match the provided filters for source '{source.AdapterKey}'.");
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
                row.StatusKey,
                row.StatusLabel))
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

    private static async Task<IResult> GetInstitutionsAsync(
        [AsParameters] InstitutionsQuery query,
        BloodWatchDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var sourceKey = NormalizeRequired(query.Source);
        if (sourceKey is null)
        {
            return CreateBadRequestProblem("Query parameter 'source' is required.");
        }

        var source = await FindSourceAsync(sourceKey, dbContext, cancellationToken);
        if (source is null)
        {
            return CreateNotFoundProblem($"Source '{sourceKey}' was not found.");
        }

        var regionKey = NormalizeRequired(query.Region);
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

        var institutionsQuery =
            from institution in dbContext.DonationCenters.AsNoTracking()
            join region in dbContext.Regions.AsNoTracking() on institution.RegionId equals region.Id
            where institution.SourceId == source.Id
            select new
            {
                institution.Id,
                institution.ExternalId,
                institution.InstitutionCode,
                institution.Name,
                region.Key,
                region.DisplayName,
                institution.DistrictName,
                institution.MunicipalityName,
                institution.Address,
                institution.Latitude,
                institution.Longitude,
                institution.Schedule,
                institution.Phone,
                institution.Email,
            };

        if (regionKey is not null)
        {
            institutionsQuery = institutionsQuery.Where(row => row.Key == regionKey);
        }

        var items = await institutionsQuery
            .OrderBy(row => row.Key)
            .ThenBy(row => row.Name)
            .Select(row => new InstitutionItem(
                row.Id,
                row.ExternalId,
                row.InstitutionCode,
                row.Name,
                new RegionItem(row.Key, row.DisplayName),
                row.DistrictName,
                row.MunicipalityName,
                row.Address,
                row.Latitude,
                row.Longitude,
                row.Schedule,
                row.Phone,
                row.Email))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new InstitutionsResponse(source.AdapterKey, items));
    }

    private static async Task<IResult> GetNearestInstitutionsAsync(
        [AsParameters] NearestInstitutionsQuery query,
        BloodWatchDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var sourceKey = NormalizeRequired(query.Source);
        if (sourceKey is null)
        {
            return CreateBadRequestProblem("Query parameter 'source' is required.");
        }

        if (!query.Lat.HasValue || !query.Lon.HasValue)
        {
            return CreateBadRequestProblem("Query parameters 'lat' and 'lon' are required.");
        }

        var latitude = query.Lat.Value;
        var longitude = query.Lon.Value;

        if (latitude is < -90m or > 90m)
        {
            return CreateBadRequestProblem("Query parameter 'lat' must be between -90 and 90.");
        }

        if (longitude is < -180m or > 180m)
        {
            return CreateBadRequestProblem("Query parameter 'lon' must be between -180 and 180.");
        }

        var limit = Math.Clamp(query.Limit ?? 10, 1, 100);

        var source = await FindSourceAsync(sourceKey, dbContext, cancellationToken);
        if (source is null)
        {
            return CreateNotFoundProblem($"Source '{sourceKey}' was not found.");
        }

        var rows = await (
                from institution in dbContext.DonationCenters.AsNoTracking()
                join region in dbContext.Regions.AsNoTracking() on institution.RegionId equals region.Id
                where institution.SourceId == source.Id
                      && institution.Latitude.HasValue
                      && institution.Longitude.HasValue
                select new
                {
                    institution.Id,
                    institution.ExternalId,
                    institution.InstitutionCode,
                    institution.Name,
                    RegionKey = region.Key,
                    RegionName = region.DisplayName,
                    institution.DistrictName,
                    institution.MunicipalityName,
                    institution.Address,
                    institution.Latitude,
                    institution.Longitude,
                    institution.Schedule,
                    institution.Phone,
                    institution.Email,
                })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row =>
            {
                var distanceKm = CalculateDistanceKm(
                    latitude,
                    longitude,
                    row.Latitude!.Value,
                    row.Longitude!.Value);

                var institution = new InstitutionItem(
                    row.Id,
                    row.ExternalId,
                    row.InstitutionCode,
                    row.Name,
                    new RegionItem(row.RegionKey, row.RegionName),
                    row.DistrictName,
                    row.MunicipalityName,
                    row.Address,
                    row.Latitude,
                    row.Longitude,
                    row.Schedule,
                    row.Phone,
                    row.Email);

                return new NearestInstitutionItem(institution, distanceKm);
            })
            .OrderBy(item => item.DistanceKm)
            .ThenBy(item => item.Institution.Id)
            .Take(limit)
            .ToArray();

        return TypedResults.Ok(new NearestInstitutionsResponse(source.AdapterKey, latitude, longitude, items));
    }

    private static async Task<IResult> GetSessionsAsync(
        [AsParameters] SessionsQuery query,
        BloodWatchDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var sourceKey = NormalizeRequired(query.Source);
        if (sourceKey is null)
        {
            return CreateBadRequestProblem("Query parameter 'source' is required.");
        }

        var source = await FindSourceAsync(sourceKey, dbContext, cancellationToken);
        if (source is null)
        {
            return CreateNotFoundProblem($"Source '{sourceKey}' was not found.");
        }

        var regionKey = NormalizeRequired(query.Region);
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

        var fromDate = ResolveFromDate(query.FromDate, out var fromDateError);
        if (fromDateError is not null)
        {
            return CreateBadRequestProblem(fromDateError);
        }

        var limit = Math.Clamp(query.Limit ?? 100, 1, 500);

        var sessionsQuery =
            from session in dbContext.CollectionSessions.AsNoTracking()
            join region in dbContext.Regions.AsNoTracking() on session.RegionId equals region.Id
            join center in dbContext.DonationCenters.AsNoTracking() on session.DonationCenterId equals center.Id into centerRows
            from center in centerRows.DefaultIfEmpty()
            where session.SourceId == source.Id
                  && session.SessionDate.HasValue
                  && session.SessionDate.Value >= fromDate
            select new
            {
                session.Id,
                session.ExternalId,
                session.SessionDate,
                session.SessionHours,
                session.SessionTypeCode,
                session.SessionTypeName,
                session.StateCode,
                session.Location,
                RegionKey = region.Key,
                RegionName = region.DisplayName,
                session.InstitutionCode,
                session.InstitutionName,
                InstitutionId = (Guid?)center.Id,
                session.Latitude,
                session.Longitude,
            };

        if (regionKey is not null)
        {
            sessionsQuery = sessionsQuery.Where(row => row.RegionKey == regionKey);
        }

        var items = await sessionsQuery
            .OrderBy(row => row.SessionDate)
            .ThenBy(row => row.InstitutionName)
            .ThenBy(row => row.ExternalId)
            .Take(limit)
            .Select(row => new SessionItem(
                row.Id,
                row.ExternalId,
                row.SessionDate,
                row.SessionHours,
                string.IsNullOrWhiteSpace(row.SessionTypeName) ? row.SessionTypeCode : row.SessionTypeName,
                row.StateCode,
                row.Location,
                new RegionItem(row.RegionKey, row.RegionName),
                new SessionInstitutionItem(row.InstitutionId, row.InstitutionCode, row.InstitutionName),
                row.Latitude,
                row.Longitude))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new SessionsResponse(source.AdapterKey, fromDate, items));
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

    private static DateOnly ResolveFromDate(string? rawFromDate, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(rawFromDate))
        {
            return DateOnly.FromDateTime(DateTime.UtcNow);
        }

        if (DateOnly.TryParse(rawFromDate.Trim(), out var parsedDate))
        {
            return parsedDate;
        }

        error = "Query parameter 'fromDate' must be a valid date (for example 2026-02-20).";
        return default;
    }

    private static string? NormalizeRequired(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static double CalculateDistanceKm(decimal latA, decimal lonA, decimal latB, decimal lonB)
    {
        const double earthRadiusKm = 6371d;

        var latARad = DegreesToRadians((double)latA);
        var lonARad = DegreesToRadians((double)lonA);
        var latBRad = DegreesToRadians((double)latB);
        var lonBRad = DegreesToRadians((double)lonB);

        var deltaLat = latBRad - latARad;
        var deltaLon = lonBRad - lonARad;

        var sinLat = Math.Sin(deltaLat / 2d);
        var sinLon = Math.Sin(deltaLon / 2d);

        var a = sinLat * sinLat + Math.Cos(latARad) * Math.Cos(latBRad) * sinLon * sinLon;
        var c = 2d * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1d - a));

        return earthRadiusKm * c;
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * (Math.PI / 180d);
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
}
