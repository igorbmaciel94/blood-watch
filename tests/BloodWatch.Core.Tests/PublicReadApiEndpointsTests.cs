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
    private const string SourceKey = "pt-dador-ipst";
    private const string SourceName = "Portugal Dador/IPST";

    [Fact]
    public async Task GetHealthLive_ShouldReturnLive()
    {
        await using var factory = CreateFactory();
        await SeedAllAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(payload);
        Assert.Equal("live", payload!.Status);
    }

    [Fact]
    public async Task GetHealthReady_ShouldReturnReady()
    {
        await using var factory = CreateFactory();
        await SeedAllAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(payload);
        Assert.Equal("ready", payload!.Status);
    }

    [Fact]
    public async Task GetHealth_ShouldReturnReady()
    {
        await using var factory = CreateFactory();
        await SeedAllAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(payload);
        Assert.Equal("ready", payload!.Status);
    }

    [Fact]
    public async Task GetVersion_ShouldReturnBuildMetadata()
    {
        await using var factory = CreateFactory();
        await SeedAllAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/version");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<VersionResponse>();
        Assert.NotNull(payload);
        Assert.Equal("test-version", payload!.Version);
        Assert.Equal("test-commit", payload.Commit);
        Assert.Equal("2026-02-24T00:00:00Z", payload.BuildDate);
    }

    [Fact]
    public async Task HealthRequest_WithCorrelationHeader_ShouldEchoHeader()
    {
        await using var factory = CreateFactory();
        await SeedAllAsync(factory);
        using var client = factory.CreateClient();
        const string correlationId = "test-correlation-123";
        client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Correlation-Id", out var values));
        Assert.Equal(correlationId, values.Single());
    }

    [Fact]
    public async Task GetLatestReserves_ShouldReturnStatusOnlyPayload()
    {
        await using var factory = CreateFactory();
        await SeedAllAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/v1/reserves/latest?source={SourceKey}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<LatestReservesResponse>();
        Assert.NotNull(payload);

        var item = Assert.Single(payload!.Items, i => i.Region.Key == "pt-norte" && i.Metric == "blood-group-o-minus");
        Assert.Equal("critical", item.StatusKey);
        Assert.Equal("Critical", item.StatusLabel);
    }

    [Fact]
    public async Task GetNearestInstitutions_ShouldReturnDeterministicOrdering()
    {
        await using var factory = CreateFactory();
        var seeded = await SeedAllAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"/api/v1/institutions/nearest?source={SourceKey}&lat=41.165337&lon=-8.604440&limit=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<NearestInstitutionsResponse>();
        Assert.NotNull(payload);

        var items = payload!.Items.ToArray();
        Assert.Equal(2, items.Length);
        Assert.Equal(seeded.CenterAId, items[0].Institution.Id);
        Assert.Equal(seeded.CenterBId, items[1].Institution.Id);
        Assert.True(items[0].DistanceKm <= items[1].DistanceKm);
    }

    [Fact]
    public async Task GetInstitutions_WithRegionFilter_ShouldFilterByRegion()
    {
        await using var factory = CreateFactory();
        await SeedAllAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/v1/institutions?source={SourceKey}&region=pt-norte");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<InstitutionsResponse>();
        Assert.NotNull(payload);
        Assert.NotEmpty(payload!.Items);
        Assert.All(payload.Items, item => Assert.Equal("pt-norte", item.Region.Key));
    }

    [Fact]
    public async Task GetSessions_ShouldReturnUpcomingOnly()
    {
        await using var factory = CreateFactory();
        await SeedAllAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/v1/sessions?source={SourceKey}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SessionsResponse>();
        Assert.NotNull(payload);

        Assert.NotEmpty(payload!.Items);
        Assert.All(payload.Items, item =>
        {
            Assert.NotNull(item.Date);
            Assert.True(item.Date!.Value >= payload.FromDate);
        });
    }

    [Fact]
    public async Task GetSessions_WithRegionFilter_ShouldFilterByRegion()
    {
        await using var factory = CreateFactory();
        await SeedAllAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/v1/sessions?source={SourceKey}&region=pt-norte");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SessionsResponse>();
        Assert.NotNull(payload);
        Assert.NotEmpty(payload!.Items);
        Assert.All(payload.Items, item => Assert.Equal("pt-norte", item.Region.Key));
    }

    [Fact]
    public async Task PublicEndpoints_ShouldBeRateLimited()
    {
        await using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["BloodWatch:Api:RateLimiting:PermitLimitPerMinute"] = "1",
            ["BloodWatch:Api:RateLimiting:QueueLimit"] = "0",
        });
        await SeedAllAsync(factory);
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
    public async Task OpenApi_ShouldContainInstitutionsAndSessionsEndpoints()
    {
        await using var factory = CreateFactory();
        await SeedAllAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var openApiDocument = await response.Content.ReadAsStringAsync();
        Assert.Contains("/api/v1/reserves/latest", openApiDocument, StringComparison.Ordinal);
        Assert.Contains("/api/v1/institutions", openApiDocument, StringComparison.Ordinal);
        Assert.Contains("/api/v1/institutions/nearest", openApiDocument, StringComparison.Ordinal);
        Assert.Contains("/api/v1/sessions", openApiDocument, StringComparison.Ordinal);
        Assert.DoesNotContain("institutionRegion", openApiDocument, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionRegion", openApiDocument, StringComparison.Ordinal);
    }

    private static ApiWebApplicationFactory CreateFactory(IReadOnlyDictionary<string, string?>? overrides = null)
    {
        return new ApiWebApplicationFactory(overrides);
    }

    private static async Task<SeededData> SeedAllAsync(ApiWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BloodWatchDbContext>();

        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var source = await EnsureSourceExistsAsync(dbContext);

        var northRegionId = Guid.Parse("00000000-0000-0000-0000-000000000011");
        var centerRegionId = Guid.Parse("00000000-0000-0000-0000-000000000012");

        dbContext.Regions.AddRange(
            new RegionEntity
            {
                Id = northRegionId,
                SourceId = source.Id,
                Key = "pt-norte",
                DisplayName = "Norte",
                CreatedAtUtc = DateTime.UtcNow,
            },
            new RegionEntity
            {
                Id = centerRegionId,
                SourceId = source.Id,
                Key = "pt-centro",
                DisplayName = "Centro",
                CreatedAtUtc = DateTime.UtcNow,
            });

        var capturedAtUtc = DateTime.UtcNow.AddMinutes(-15);
        dbContext.CurrentReserves.Add(new CurrentReserveEntity
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            RegionId = northRegionId,
            MetricKey = "blood-group-o-minus",
            StatusKey = "critical",
            StatusLabel = "Critical",
            ReferenceDate = DateOnly.FromDateTime(DateTime.UtcNow),
            CapturedAtUtc = capturedAtUtc,
            UpdatedAtUtc = DateTime.UtcNow,
        });

        var centerAId = Guid.Parse("00000000-0000-0000-0000-000000000101");
        var centerBId = Guid.Parse("00000000-0000-0000-0000-000000000102");

        dbContext.DonationCenters.AddRange(
            new DonationCenterEntity
            {
                Id = centerAId,
                SourceId = source.Id,
                RegionId = northRegionId,
                ExternalId = "1",
                InstitutionCode = "SP",
                Name = "CST Porto A",
                Latitude = 41.165337m,
                Longitude = -8.604440m,
                UpdatedAtUtc = DateTime.UtcNow,
            },
            new DonationCenterEntity
            {
                Id = centerBId,
                SourceId = source.Id,
                RegionId = northRegionId,
                ExternalId = "2",
                InstitutionCode = "SC",
                Name = "CST Porto B",
                Latitude = 41.165337m,
                Longitude = -8.604440m,
                UpdatedAtUtc = DateTime.UtcNow,
            });

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        dbContext.CollectionSessions.AddRange(
            new CollectionSessionEntity
            {
                Id = Guid.NewGuid(),
                SourceId = source.Id,
                RegionId = northRegionId,
                DonationCenterId = centerAId,
                ExternalId = "session-future",
                InstitutionCode = "SP",
                InstitutionName = "CST Porto A",
                SessionDate = today.AddDays(1),
                SessionHours = "08:00 19:30",
                SessionTypeName = "POSTO FIXO",
                StateCode = "P",
                UpdatedAtUtc = DateTime.UtcNow,
            },
            new CollectionSessionEntity
            {
                Id = Guid.NewGuid(),
                SourceId = source.Id,
                RegionId = northRegionId,
                DonationCenterId = centerAId,
                ExternalId = "session-past",
                InstitutionCode = "SP",
                InstitutionName = "CST Porto A",
                SessionDate = today.AddDays(-1),
                SessionHours = "08:00 19:30",
                SessionTypeName = "POSTO FIXO",
                StateCode = "P",
                UpdatedAtUtc = DateTime.UtcNow,
            });

        await dbContext.SaveChangesAsync();

        return new SeededData(centerAId, centerBId);
    }

    private static async Task<SourceEntity> EnsureSourceExistsAsync(BloodWatchDbContext dbContext)
    {
        var existing = await dbContext.Sources.SingleOrDefaultAsync(source => source.AdapterKey == SourceKey);
        if (existing is not null)
        {
            return existing;
        }

        var source = new SourceEntity
        {
            Id = Guid.NewGuid(),
            AdapterKey = SourceKey,
            Name = SourceName,
            CreatedAtUtc = DateTime.UtcNow,
            LastPolledAtUtc = null,
        };

        dbContext.Sources.Add(source);
        await dbContext.SaveChangesAsync();
        return source;
    }

    private sealed record SeededData(Guid CenterAId, Guid CenterBId);

    private sealed record HealthResponse(string Status);
    private sealed record VersionResponse(string Version, string Commit, string BuildDate);

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
                    ["BloodWatch:Build:Version"] = "test-version",
                    ["BloodWatch:Build:Commit"] = "test-commit",
                    ["BloodWatch:Build:Date"] = "2026-02-24T00:00:00Z",
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
