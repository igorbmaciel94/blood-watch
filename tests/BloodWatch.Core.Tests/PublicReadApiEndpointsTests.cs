using System.Net;
using System.Net.Http.Json;
using BloodWatch.Api.Contracts;
using BloodWatch.Infrastructure.Persistence;
using BloodWatch.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BloodWatch.Core.Tests;

public sealed class PublicReadApiEndpointsTests
{
    private const string SourceKey = "pt-transparencia-sns";
    private const string SourceName = "Portugal SNS Transparency";

    [Fact]
    public async Task GetHealth_ShouldReturnHealthy()
    {
        await using var factory = CreateFactory();
        await SeedEmptySourceAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(payload);
        Assert.Equal("healthy", payload!.Status);
    }

    [Fact]
    public async Task GetSources_ShouldReturnSourceList()
    {
        await using var factory = CreateFactory();
        await SeedEmptySourceAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/sources");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SourcesResponse>();
        Assert.NotNull(payload);

        var items = payload!.Items.ToArray();
        Assert.Single(items);
        Assert.Equal(SourceKey, items[0].Source);
        Assert.Equal(SourceName, items[0].Name);
    }

    [Fact]
    public async Task GetRegions_MissingSource_ShouldReturnBadRequestProblem()
    {
        await using var factory = CreateFactory();
        await SeedEmptySourceAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/regions");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal("Bad request", problem.Title);
    }

    [Fact]
    public async Task GetRegions_UnknownSource_ShouldReturnNotFoundProblem()
    {
        await using var factory = CreateFactory();
        await SeedEmptySourceAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/regions?source=unknown");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal("Not found", problem.Title);
    }

    [Fact]
    public async Task GetLatestReserves_ShouldReturnCurrentReserves()
    {
        await using var factory = CreateFactory();
        await SeedSourceWithCurrentReservesAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/v1/reserves/latest?source={SourceKey}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<LatestReservesResponse>();
        Assert.NotNull(payload);

        var items = payload!.Items.ToArray();
        Assert.Equal(SourceKey, payload.Source);
        Assert.Equal(new DateOnly(2026, 2, 1), payload.ReferenceDate);
        Assert.Equal(3, items.Length);
        Assert.Equal("pt-centro", items[0].Region.Key);
        Assert.Equal("pt-norte", items[1].Region.Key);
        Assert.Equal("blood-group-a-plus", items[1].Metric);
    }

    [Fact]
    public async Task GetLatestReserves_MissingSource_ShouldReturnBadRequestProblem()
    {
        await using var factory = CreateFactory();
        await SeedSourceWithCurrentReservesAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/reserves/latest?region=pt-norte");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal("Bad request", problem.Title);
    }

    [Fact]
    public async Task GetLatestReserves_UnknownSource_ShouldReturnNotFoundProblem()
    {
        await using var factory = CreateFactory();
        await SeedSourceWithCurrentReservesAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/reserves/latest?source=unknown-source");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal("Not found", problem.Title);
    }

    [Fact]
    public async Task GetLatestReserves_WithRegionFilter_ShouldReturnRegionRowsOnly()
    {
        await using var factory = CreateFactory();
        await SeedSourceWithCurrentReservesAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/v1/reserves/latest?source={SourceKey}&region=pt-norte");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<LatestReservesResponse>();
        Assert.NotNull(payload);

        var items = payload!.Items.ToArray();
        Assert.Equal(2, items.Length);
        Assert.All(items, item => Assert.Equal("pt-norte", item.Region.Key));
    }

    [Fact]
    public async Task GetLatestReserves_WithMetricFilter_ShouldReturnMetricAcrossRegions()
    {
        await using var factory = CreateFactory();
        await SeedSourceWithCurrentReservesAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/v1/reserves/latest?source={SourceKey}&metric=overall");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<LatestReservesResponse>();
        Assert.NotNull(payload);

        var items = payload!.Items.ToArray();
        Assert.Equal(2, items.Length);
        Assert.All(items, item => Assert.Equal("overall", item.Metric));
    }

    [Fact]
    public async Task GetLatestReserves_WithRegionAndMetric_ShouldReturnSingleItem()
    {
        await using var factory = CreateFactory();
        await SeedSourceWithCurrentReservesAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"/api/v1/reserves/latest?source={SourceKey}&region=pt-norte&metric=blood-group-a-plus");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<LatestReservesResponse>();
        Assert.NotNull(payload);

        var items = payload!.Items.ToArray();
        Assert.Single(items);
        Assert.Equal("pt-norte", items[0].Region.Key);
        Assert.Equal("blood-group-a-plus", items[0].Metric);
    }

    [Fact]
    public async Task GetLatestReserves_UnknownRegionForSource_ShouldReturnNotFoundProblem()
    {
        await using var factory = CreateFactory();
        await SeedSourceWithCurrentReservesAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/v1/reserves/latest?source={SourceKey}&region=pt-unknown");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal("Not found", problem.Title);
    }

    [Fact]
    public async Task GetLatestReserves_UnknownMetricForSource_ShouldReturnNotFoundProblem()
    {
        await using var factory = CreateFactory();
        await SeedSourceWithCurrentReservesAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/v1/reserves/latest?source={SourceKey}&metric=unknown");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal("Not found", problem.Title);
    }

    [Fact]
    public async Task GetLatestReserves_ValidFiltersWithNoMatchingRow_ShouldReturnNotFoundProblem()
    {
        await using var factory = CreateFactory();
        await SeedSourceWithCurrentReservesAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"/api/v1/reserves/latest?source={SourceKey}&region=pt-centro&metric=blood-group-a-plus");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Contains("No current reserves match", problem.Detail!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetLatestReserves_WhenNoCurrentReserves_ShouldReturnNotFoundProblem()
    {
        await using var factory = CreateFactory();
        await SeedEmptySourceAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/v1/reserves/latest?source={SourceKey}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Contains("current reserves", problem.Detail!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetLatestReserves_ShouldUseMemoryCacheWithinTtl()
    {
        await using var factory = CreateFactory();
        var seeded = await SeedSourceWithCurrentReservesAsync(factory);
        using var client = factory.CreateClient();

        using var firstResponse = await client.GetAsync($"/api/v1/reserves/latest?source={SourceKey}");
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var firstPayload = await firstResponse.Content.ReadFromJsonAsync<LatestReservesResponse>();
        Assert.NotNull(firstPayload);
        var firstNorthOverall = firstPayload!.Items.Single(item => item.Region.Key == "pt-norte" && item.Metric == "overall");
        Assert.Equal(120m, firstNorthOverall.Value);

        await UpdateCurrentReserveValueAsync(
            factory,
            seeded.SourceId,
            seeded.RegionNorthId,
            metricKey: "overall",
            newValue: 999m,
            newCapturedAtUtc: DateTime.UtcNow.AddMinutes(5));

        using var secondResponse = await client.GetAsync($"/api/v1/reserves/latest?source={SourceKey}");
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var secondPayload = await secondResponse.Content.ReadFromJsonAsync<LatestReservesResponse>();
        Assert.NotNull(secondPayload);
        var secondNorthOverall = secondPayload!.Items.Single(item => item.Region.Key == "pt-norte" && item.Metric == "overall");
        Assert.Equal(120m, secondNorthOverall.Value);
    }

    [Fact]
    public async Task GetLatestReserves_CacheShouldBeScopedByFilters()
    {
        await using var factory = CreateFactory();
        var seeded = await SeedSourceWithCurrentReservesAsync(factory);
        using var client = factory.CreateClient();

        using var sourceOnlyResponse = await client.GetAsync($"/api/v1/reserves/latest?source={SourceKey}");
        Assert.Equal(HttpStatusCode.OK, sourceOnlyResponse.StatusCode);

        await UpdateCurrentReserveValueAsync(
            factory,
            seeded.SourceId,
            seeded.RegionNorthId,
            metricKey: "overall",
            newValue: 888m,
            newCapturedAtUtc: DateTime.UtcNow.AddMinutes(5));

        using var filteredResponse = await client.GetAsync($"/api/v1/reserves/latest?source={SourceKey}&region=pt-norte");
        Assert.Equal(HttpStatusCode.OK, filteredResponse.StatusCode);
        var filteredPayload = await filteredResponse.Content.ReadFromJsonAsync<LatestReservesResponse>();
        Assert.NotNull(filteredPayload);
        var northOverall = filteredPayload!.Items.Single(item => item.Region.Key == "pt-norte" && item.Metric == "overall");
        Assert.Equal(888m, northOverall.Value);
    }

    [Fact]
    public async Task GetReservesTrend_ShouldReturnNotFoundBecauseRouteWasRemoved()
    {
        await using var factory = CreateFactory();
        await SeedSourceWithCurrentReservesAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/v1/reserves/trend?source={SourceKey}&days=1&metric=overall");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PublicEndpoints_ShouldBeRateLimited()
    {
        await using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["BloodWatch:Api:RateLimiting:PermitLimitPerMinute"] = "1",
            ["BloodWatch:Api:RateLimiting:QueueLimit"] = "0",
        });
        await SeedEmptySourceAsync(factory);
        using var client = factory.CreateClient();

        using var first = await client.GetAsync("/api/v1/sources");
        using var second = await client.GetAsync("/api/v1/sources");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);

        var problem = await second.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status429TooManyRequests, problem!.Status);
    }

    [Fact]
    public async Task OpenApi_ShouldContainLatestEndpointsAndExcludeTrend()
    {
        await using var factory = CreateFactory();
        await SeedEmptySourceAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var openApiDocument = await response.Content.ReadAsStringAsync();
        Assert.Contains("/api/v1/sources", openApiDocument, StringComparison.Ordinal);
        Assert.Contains("/api/v1/regions", openApiDocument, StringComparison.Ordinal);
        Assert.Contains("/api/v1/reserves/latest", openApiDocument, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/v1/reserves/trend", openApiDocument, StringComparison.Ordinal);
    }

    private static async Task<ProblemDetails> ReadProblemAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        return problem!;
    }

    private static ApiWebApplicationFactory CreateFactory(IReadOnlyDictionary<string, string?>? overrides = null)
    {
        return new ApiWebApplicationFactory(overrides);
    }

    private static async Task SeedEmptySourceAsync(ApiWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BloodWatchDbContext>();

        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();
        await EnsureSourceExistsAsync(dbContext);
    }

    private static async Task<SeededSourceData> SeedSourceWithCurrentReservesAsync(ApiWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BloodWatchDbContext>();

        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var sourceId = await EnsureSourceExistsAsync(dbContext);
        var createdAtUtc = DateTime.UtcNow.AddDays(-5);
        var northRegionId = Guid.NewGuid();
        var centerRegionId = Guid.NewGuid();

        dbContext.Regions.AddRange(
            new RegionEntity
            {
                Id = northRegionId,
                SourceId = sourceId,
                Key = "pt-norte",
                DisplayName = "Regiao de Saude Norte",
                CreatedAtUtc = createdAtUtc,
            },
            new RegionEntity
            {
                Id = centerRegionId,
                SourceId = sourceId,
                Key = "pt-centro",
                DisplayName = "Regiao de Saude Centro",
                CreatedAtUtc = createdAtUtc,
            });

        await dbContext.SaveChangesAsync();

        var capturedAtUtc = DateTime.UtcNow.AddMinutes(-30);
        var updatedAtUtc = DateTime.UtcNow.AddMinutes(-29);
        dbContext.CurrentReserves.AddRange(
            new CurrentReserveEntity
            {
                Id = Guid.NewGuid(),
                SourceId = sourceId,
                RegionId = centerRegionId,
                MetricKey = "overall",
                Value = 70m,
                Unit = "units",
                ReferenceDate = new DateOnly(2026, 2, 1),
                CapturedAtUtc = capturedAtUtc,
                UpdatedAtUtc = updatedAtUtc,
            },
            new CurrentReserveEntity
            {
                Id = Guid.NewGuid(),
                SourceId = sourceId,
                RegionId = northRegionId,
                MetricKey = "blood-group-a-plus",
                Value = 30m,
                Unit = "units",
                ReferenceDate = new DateOnly(2026, 2, 1),
                CapturedAtUtc = capturedAtUtc,
                UpdatedAtUtc = updatedAtUtc,
            },
            new CurrentReserveEntity
            {
                Id = Guid.NewGuid(),
                SourceId = sourceId,
                RegionId = northRegionId,
                MetricKey = "overall",
                Value = 120m,
                Unit = "units",
                ReferenceDate = new DateOnly(2026, 2, 1),
                CapturedAtUtc = capturedAtUtc,
                UpdatedAtUtc = updatedAtUtc,
            });

        await dbContext.SaveChangesAsync();

        return new SeededSourceData(sourceId, northRegionId);
    }

    private static async Task UpdateCurrentReserveValueAsync(
        ApiWebApplicationFactory factory,
        Guid sourceId,
        Guid regionId,
        string metricKey,
        decimal newValue,
        DateTime newCapturedAtUtc)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BloodWatchDbContext>();

        var row = await dbContext.CurrentReserves.SingleAsync(entry =>
            entry.SourceId == sourceId
            && entry.RegionId == regionId
            && entry.MetricKey == metricKey);

        row.Value = newValue;
        row.CapturedAtUtc = newCapturedAtUtc;
        row.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();
    }

    private static async Task<Guid> EnsureSourceExistsAsync(BloodWatchDbContext dbContext)
    {
        var existing = await dbContext.Sources
            .SingleOrDefaultAsync(source => source.AdapterKey == SourceKey);
        if (existing is not null)
        {
            return existing.Id;
        }

        var source = new SourceEntity
        {
            Id = Guid.NewGuid(),
            AdapterKey = SourceKey,
            Name = SourceName,
            CreatedAtUtc = DateTime.UtcNow,
        };

        dbContext.Sources.Add(source);
        await dbContext.SaveChangesAsync();
        return source.Id;
    }

    private sealed record SeededSourceData(Guid SourceId, Guid RegionNorthId);

    private sealed record HealthResponse(string Status);

    private sealed class ApiWebApplicationFactory(IReadOnlyDictionary<string, string?>? overrides)
        : WebApplicationFactory<Program>
    {
        private readonly string _databaseName = $"bloodwatch-api-tests-{Guid.NewGuid():N}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Environment.SetEnvironmentVariable("BloodWatch__Persistence__Provider", "InMemory");
            Environment.SetEnvironmentVariable("BloodWatch__Persistence__InMemoryDatabaseName", _databaseName);

            builder.UseSetting("BloodWatch:Persistence:Provider", "InMemory");
            builder.UseSetting("BloodWatch:Persistence:InMemoryDatabaseName", _databaseName);

            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var defaults = new Dictionary<string, string?>
                {
                    ["BloodWatch:Persistence:Provider"] = "InMemory",
                    ["BloodWatch:Persistence:InMemoryDatabaseName"] = _databaseName,
                    ["BloodWatch:Api:Caching:LatestTtlSeconds"] = "60",
                    ["BloodWatch:Api:RateLimiting:PermitLimitPerMinute"] = "1000",
                    ["BloodWatch:Api:RateLimiting:QueueLimit"] = "0",
                };

                if (overrides is not null)
                {
                    foreach (var pair in overrides)
                    {
                        defaults[pair.Key] = pair.Value;
                    }
                }

                configuration.AddInMemoryCollection(defaults);
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Environment.SetEnvironmentVariable("BloodWatch__Persistence__Provider", null);
                Environment.SetEnvironmentVariable("BloodWatch__Persistence__InMemoryDatabaseName", null);
            }

            base.Dispose(disposing);
        }
    }
}
