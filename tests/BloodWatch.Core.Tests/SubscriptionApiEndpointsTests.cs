using System.Net;
using System.Net.Http.Json;
using BloodWatch.Api.Contracts;
using BloodWatch.Infrastructure.Persistence;
using BloodWatch.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BloodWatch.Core.Tests;

public sealed class SubscriptionApiEndpointsTests
{
    private const string ApiKey = "test-write-key";
    private const string SourceKey = "pt-dador-ipst";
    private const string SourceName = "Portugal Dador/IPST";

    [Fact]
    public async Task PostSubscription_WithoutApiKey_ShouldReturnUnauthorized()
    {
        await using var factory = CreateFactory();
        var seeded = await SeedSourceRegionMetricAndInstitutionAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            source = SourceKey,
            type = "discord-webhook",
            target = "https://discord.com/api/webhooks/123/token",
            scopeType = "region",
            region = "pt-norte",
            metric = "blood-group-o-minus",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostRegionSubscription_ShouldCreateSuccessfully()
    {
        await using var factory = CreateFactory();
        await SeedSourceRegionMetricAndInstitutionAsync(factory);
        using var client = CreateAuthenticatedClient(factory);

        using var response = await client.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            source = SourceKey,
            type = "discord-webhook",
            target = "https://discord.com/api/webhooks/123/token",
            scopeType = "region",
            region = "pt-norte",
            metric = "blood-group-o-minus",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<SubscriptionResponse>();
        Assert.NotNull(created);
        Assert.Equal("region", created!.ScopeType);
        Assert.Equal("pt-norte", created.Region);
        Assert.Null(created.InstitutionId);
        Assert.Equal("https://discord.com/api/webhooks/***", created.Target);
    }

    [Fact]
    public async Task PostInstitutionSubscription_WithoutMetric_ShouldCreateWildcardSuccessfully()
    {
        await using var factory = CreateFactory();
        var seeded = await SeedSourceRegionMetricAndInstitutionAsync(factory);
        using var client = CreateAuthenticatedClient(factory);

        using var response = await client.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            source = SourceKey,
            type = "discord-webhook",
            target = "https://discord.com/api/webhooks/123/token",
            scopeType = "institution",
            institutionId = seeded.InstitutionId,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<SubscriptionResponse>();
        Assert.NotNull(created);
        Assert.Equal("institution", created!.ScopeType);
        Assert.Equal(seeded.InstitutionId, created.InstitutionId);
        Assert.Null(created.Region);
        Assert.Null(created.Metric);
    }

    [Fact]
    public async Task PostSubscription_MissingScopeType_ShouldReturnBadRequest()
    {
        await using var factory = CreateFactory();
        await SeedSourceRegionMetricAndInstitutionAsync(factory);
        using var client = CreateAuthenticatedClient(factory);

        using var response = await client.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            source = SourceKey,
            type = "discord-webhook",
            target = "https://discord.com/api/webhooks/123/token",
            region = "pt-norte",
            metric = "blood-group-o-minus",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Bad request", problem!.Title);
    }

    [Fact]
    public async Task PostInstitutionSubscription_WithUnknownInstitution_ShouldReturnNotFound()
    {
        await using var factory = CreateFactory();
        await SeedSourceRegionMetricAndInstitutionAsync(factory);
        using var client = CreateAuthenticatedClient(factory);

        using var response = await client.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            source = SourceKey,
            type = "discord-webhook",
            target = "https://discord.com/api/webhooks/123/token",
            scopeType = "institution",
            institutionId = Guid.NewGuid(),
            metric = "blood-group-o-minus",
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetSubscriptions_ShouldReturnScopeFields()
    {
        await using var factory = CreateFactory();
        var seeded = await SeedSourceRegionMetricAndInstitutionAsync(factory);
        using var client = CreateAuthenticatedClient(factory);

        await client.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            source = SourceKey,
            type = "discord-webhook",
            target = "https://discord.com/api/webhooks/123/token",
            scopeType = "region",
            region = "pt-norte",
            metric = "blood-group-o-minus",
        });

        await client.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            source = SourceKey,
            type = "discord-webhook",
            target = "https://discord.com/api/webhooks/123/token",
            scopeType = "institution",
            institutionId = seeded.InstitutionId,
        });

        using var response = await client.GetAsync("/api/v1/subscriptions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<SubscriptionsResponse>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Items.Count);
        Assert.Contains(payload.Items, item => item.ScopeType == "region" && item.Region == "pt-norte");
        Assert.Contains(payload.Items, item =>
            item.ScopeType == "institution"
            && item.InstitutionId == seeded.InstitutionId
            && item.Metric is null);
    }

    [Fact]
    public async Task PostSubscription_WithExplicitMetric_ShouldPersistExactMetric()
    {
        await using var factory = CreateFactory();
        var seeded = await SeedSourceRegionMetricAndInstitutionAsync(factory);
        using var client = CreateAuthenticatedClient(factory);

        using var response = await client.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            source = SourceKey,
            type = "discord-webhook",
            target = "https://discord.com/api/webhooks/123/token",
            scopeType = "institution",
            institutionId = seeded.InstitutionId,
            metric = "blood-group-o-minus",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SubscriptionResponse>();
        Assert.NotNull(payload);
        Assert.Equal("blood-group-o-minus", payload!.Metric);
    }

    [Fact]
    public async Task GetSubscriptions_WithExplicitMetricFilter_ShouldReturnOnlyExactMetricSubscriptions()
    {
        await using var factory = CreateFactory();
        var seeded = await SeedSourceRegionMetricAndInstitutionAsync(factory);
        using var client = CreateAuthenticatedClient(factory);

        await client.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            source = SourceKey,
            type = "discord-webhook",
            target = "https://discord.com/api/webhooks/123/token",
            scopeType = "institution",
            institutionId = seeded.InstitutionId,
        });

        await client.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            source = SourceKey,
            type = "discord-webhook",
            target = "https://discord.com/api/webhooks/123/token",
            scopeType = "institution",
            institutionId = seeded.InstitutionId,
            metric = "blood-group-o-minus",
        });

        using var response = await client.GetAsync("/api/v1/subscriptions?metric=blood-group-o-minus");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SubscriptionsResponse>();
        Assert.NotNull(payload);
        Assert.Single(payload!.Items);
        Assert.Equal("blood-group-o-minus", payload.Items.Single().Metric);
    }

    [Fact]
    public async Task DeleteSubscription_ShouldSoftDisable()
    {
        await using var factory = CreateFactory();
        await SeedSourceRegionMetricAndInstitutionAsync(factory);
        using var client = CreateAuthenticatedClient(factory);

        using var postResponse = await client.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            source = SourceKey,
            type = "discord-webhook",
            target = "https://discord.com/api/webhooks/123/token",
            scopeType = "region",
            region = "pt-norte",
            metric = "blood-group-o-minus",
        });

        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);
        var created = await postResponse.Content.ReadFromJsonAsync<SubscriptionResponse>();
        Assert.NotNull(created);

        using var deleteResponse = await client.DeleteAsync($"/api/v1/subscriptions/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BloodWatchDbContext>();
        var row = await dbContext.Subscriptions.SingleAsync(entry => entry.Id == created.Id);
        Assert.False(row.IsEnabled);
        Assert.NotNull(row.DisabledAtUtc);
    }

    private static HttpClient CreateAuthenticatedClient(ApiWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", ApiKey);
        return client;
    }

    private static ApiWebApplicationFactory CreateFactory()
    {
        return new ApiWebApplicationFactory();
    }

    private static async Task<SeededData> SeedSourceRegionMetricAndInstitutionAsync(ApiWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BloodWatchDbContext>();

        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var source = await EnsureSourceExistsAsync(dbContext);

        var region = new RegionEntity
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            Key = "pt-norte",
            DisplayName = "Norte",
            CreatedAtUtc = DateTime.UtcNow,
        };

        dbContext.Regions.Add(region);

        dbContext.CurrentReserves.Add(new CurrentReserveEntity
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            RegionId = region.Id,
            MetricKey = "blood-group-o-minus",
            StatusKey = "warning",
            StatusLabel = "Warning",
            ReferenceDate = DateOnly.FromDateTime(DateTime.UtcNow),
            CapturedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });

        var institution = new DonationCenterEntity
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            RegionId = region.Id,
            ExternalId = "inst-1",
            InstitutionCode = "SP",
            Name = "CST Porto",
            UpdatedAtUtc = DateTime.UtcNow,
        };

        dbContext.DonationCenters.Add(institution);
        await dbContext.SaveChangesAsync();

        return new SeededData(institution.Id);
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

    private sealed record SeededData(Guid InstitutionId);

    private sealed class ApiWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _databaseName = $"bloodwatch-subscriptions-tests-{Guid.NewGuid():N}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Environment.SetEnvironmentVariable("BloodWatch__Persistence__Provider", "InMemory");
            Environment.SetEnvironmentVariable("BloodWatch__Persistence__InMemoryDatabaseName", _databaseName);

            builder.UseSetting("BloodWatch:Persistence:Provider", "InMemory");
            builder.UseSetting("BloodWatch:Persistence:InMemoryDatabaseName", _databaseName);

            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BloodWatch:Persistence:Provider"] = "InMemory",
                    ["BloodWatch:Persistence:InMemoryDatabaseName"] = _databaseName,
                    ["BLOODWATCH:API_KEY"] = ApiKey,
                    ["BloodWatch:Api:Caching:LatestTtlSeconds"] = "60",
                    ["BloodWatch:Api:RateLimiting:PermitLimitPerMinute"] = "1000",
                    ["BloodWatch:Api:RateLimiting:QueueLimit"] = "0",
                });
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
