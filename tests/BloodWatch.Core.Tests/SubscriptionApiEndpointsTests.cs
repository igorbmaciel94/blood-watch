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
    private const string SourceKey = "pt-transparencia-sns";
    private const string SourceName = "Portugal SNS Transparency";

    [Fact]
    public async Task PostSubscription_WithoutApiKey_ShouldReturnUnauthorized()
    {
        await using var factory = CreateFactory();
        await SeedSourceRegionAndMetricAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            source = SourceKey,
            type = "discord-webhook",
            target = "https://discord.com/api/webhooks/123/token",
            region = "pt-norte",
            metric = "overall",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetSubscriptions_WithoutApiKey_ShouldReturnUnauthorized()
    {
        await using var factory = CreateFactory();
        await SeedSourceRegionAndMetricAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/subscriptions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostSubscription_MissingRegion_ShouldReturnBadRequest()
    {
        await using var factory = CreateFactory();
        await SeedSourceRegionAndMetricAsync(factory);
        using var client = CreateAuthenticatedClient(factory);

        using var response = await client.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            source = SourceKey,
            type = "discord-webhook",
            target = "https://discord.com/api/webhooks/123/token",
            metric = "overall",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Bad request", problem!.Title);
    }

    [Fact]
    public async Task PostSubscription_MissingMetric_ShouldReturnBadRequest()
    {
        await using var factory = CreateFactory();
        await SeedSourceRegionAndMetricAsync(factory);
        using var client = CreateAuthenticatedClient(factory);

        using var response = await client.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            source = SourceKey,
            type = "discord-webhook",
            target = "https://discord.com/api/webhooks/123/token",
            region = "pt-norte",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Bad request", problem!.Title);
    }

    [Fact]
    public async Task PostSubscription_InvalidTarget_ShouldReturnBadRequest()
    {
        await using var factory = CreateFactory();
        await SeedSourceRegionAndMetricAsync(factory);
        using var client = CreateAuthenticatedClient(factory);

        using var response = await client.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            source = SourceKey,
            type = "discord-webhook",
            target = "https://example.com/api/webhooks/123/token",
            region = "pt-norte",
            metric = "overall",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostAndGetSubscription_ShouldReturnMaskedTarget()
    {
        await using var factory = CreateFactory();
        await SeedSourceRegionAndMetricAsync(factory);
        using var client = CreateAuthenticatedClient(factory);

        using var postResponse = await client.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            source = SourceKey,
            type = "discord-webhook",
            target = "https://discord.com/api/webhooks/123/token",
            region = "pt-norte",
            metric = "overall",
        });

        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);
        var created = await postResponse.Content.ReadFromJsonAsync<SubscriptionResponse>();
        Assert.NotNull(created);
        Assert.Equal("https://discord.com/api/webhooks/***", created!.Target);

        using var getResponse = await client.GetAsync($"/api/v1/subscriptions/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var loaded = await getResponse.Content.ReadFromJsonAsync<SubscriptionResponse>();
        Assert.NotNull(loaded);
        Assert.Equal(created.Id, loaded!.Id);
        Assert.Equal("https://discord.com/api/webhooks/***", loaded.Target);
        Assert.Equal("pt-norte", loaded.Region);
        Assert.Equal("overall", loaded.Metric);
    }

    [Fact]
    public async Task GetSubscriptions_ShouldReturnAllWithMaskedTargets()
    {
        await using var factory = CreateFactory();
        await SeedSourceRegionAndMetricAsync(factory);
        using var client = CreateAuthenticatedClient(factory);

        using var createResponse = await client.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            source = SourceKey,
            type = "discord-webhook",
            target = "https://discord.com/api/webhooks/123/token",
            region = "pt-norte",
            metric = "overall",
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        using var response = await client.GetAsync("/api/v1/subscriptions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<SubscriptionsResponse>();
        Assert.NotNull(payload);
        var items = payload!.Items.ToArray();
        Assert.Single(items);
        Assert.Equal("https://discord.com/api/webhooks/***", items[0].Target);
        Assert.Equal("pt-norte", items[0].Region);
        Assert.Equal("overall", items[0].Metric);
    }

    [Fact]
    public async Task GetSubscriptions_ShouldExcludeDisabledSubscriptions()
    {
        await using var factory = CreateFactory();
        await SeedSourceRegionAndMetricAsync(factory);
        using var client = CreateAuthenticatedClient(factory);

        using var createResponse = await client.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            source = SourceKey,
            type = "discord-webhook",
            target = "https://discord.com/api/webhooks/123/token",
            region = "pt-norte",
            metric = "overall",
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<SubscriptionResponse>();
        Assert.NotNull(created);

        using var deleteResponse = await client.DeleteAsync($"/api/v1/subscriptions/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using var listResponse = await client.GetAsync("/api/v1/subscriptions");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var payload = await listResponse.Content.ReadFromJsonAsync<SubscriptionsResponse>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task DeleteSubscription_ShouldSoftDisable()
    {
        await using var factory = CreateFactory();
        await SeedSourceRegionAndMetricAsync(factory);
        using var client = CreateAuthenticatedClient(factory);

        using var postResponse = await client.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            source = SourceKey,
            type = "discord-webhook",
            target = "https://discord.com/api/webhooks/123/token",
            region = "pt-norte",
            metric = "overall",
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

    private static async Task SeedSourceRegionAndMetricAsync(ApiWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BloodWatchDbContext>();

        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var sourceId = await EnsureSourceExistsAsync(dbContext);
        var regionId = Guid.NewGuid();

        dbContext.Regions.Add(new RegionEntity
        {
            Id = regionId,
            SourceId = sourceId,
            Key = "pt-norte",
            DisplayName = "Regiao de Saude Norte",
            CreatedAtUtc = DateTime.UtcNow,
        });

        dbContext.CurrentReserves.Add(new CurrentReserveEntity
        {
            Id = Guid.NewGuid(),
            SourceId = sourceId,
            RegionId = regionId,
            MetricKey = "overall",
            Value = 123m,
            Unit = "units",
            Severity = null,
            ReferenceDate = new DateOnly(2026, 2, 1),
            CapturedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });

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
            LastPolledAtUtc = null,
        };

        dbContext.Sources.Add(source);
        await dbContext.SaveChangesAsync();
        return source.Id;
    }

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
