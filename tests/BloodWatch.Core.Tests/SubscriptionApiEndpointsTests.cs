using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using BloodWatch.Api.Contracts;
using BloodWatch.Infrastructure.Persistence;
using BloodWatch.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace BloodWatch.Core.Tests;

public sealed class SubscriptionApiEndpointsTests
{
    private const string AdminEmail = "igorbmaciel@yahoo.com.br";
    private const string AdminPassword = "test-admin-password";
    private static readonly string AdminPasswordHash = new PasswordHasher<string>().HashPassword("bloodwatch-admin", AdminPassword);
    private static readonly string JwtSigningKey = new('t', 64);
    private const string JwtIssuer = "bloodwatch-api-tests";
    private const string JwtAudience = "bloodwatch-clients-tests";
    private const string SourceKey = "pt-dador-ipst";
    private const string SourceName = "Portugal Dador/IPST";

    [Fact]
    public async Task PostSubscription_WithoutBearer_ShouldReturnUnauthorized()
    {
        await using var factory = CreateFactory();
        await SeedSourceRegionMetricAndInstitutionAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            source = SourceKey,
            type = "discord:webhook",
            target = "https://discord.com/api/webhooks/123/token",
            scopeType = "region",
            region = "pt-norte",
            metric = "blood-group-o-minus",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostSubscription_WithApiKeyHeaderOnly_ShouldReturnUnauthorized()
    {
        await using var factory = CreateFactory();
        await SeedSourceRegionMetricAndInstitutionAsync(factory);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "legacy-key");

        using var response = await client.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            source = SourceKey,
            type = "discord:webhook",
            target = "https://discord.com/api/webhooks/123/token",
            scopeType = "region",
            region = "pt-norte",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostToken_WithValidCredentials_ShouldReturnJwtToken()
    {
        await using var factory = CreateFactory();
        await SeedSourceRegionMetricAndInstitutionAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/token", new
        {
            email = AdminEmail,
            password = AdminPassword,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<CreateTokenResponse>();
        Assert.NotNull(payload);
        Assert.Equal("Bearer", payload!.TokenType);
        Assert.NotEmpty(payload.AccessToken);
        Assert.True(payload.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(10));
    }

    [Fact]
    public async Task PostToken_WithMissingEmailOrPassword_ShouldReturnBadRequest()
    {
        await using var factory = CreateFactory();
        await SeedSourceRegionMetricAndInstitutionAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/token", new
        {
            email = AdminEmail,
            password = "",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostToken_WithInvalidEmail_ShouldReturnUnauthorized()
    {
        await using var factory = CreateFactory();
        await SeedSourceRegionMetricAndInstitutionAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/token", new
        {
            email = "unknown@example.com",
            password = AdminPassword,
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostToken_WithInvalidPassword_ShouldReturnUnauthorized()
    {
        await using var factory = CreateFactory();
        await SeedSourceRegionMetricAndInstitutionAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/token", new
        {
            email = AdminEmail,
            password = "wrong-password",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostToken_WithLegacyLoginKeyPayload_ShouldReturnBadRequest()
    {
        await using var factory = CreateFactory();
        await SeedSourceRegionMetricAndInstitutionAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/token", new
        {
            loginKey = "legacy-key",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostToken_WithMissingJwtConfiguration_ShouldReturnServiceUnavailable()
    {
        await using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["BloodWatch:JwtAuth:SigningKey"] = string.Empty,
            ["BloodWatch:JwtAuth:AdminEmail"] = string.Empty,
            ["BloodWatch:JwtAuth:AdminPasswordHash"] = string.Empty,
        });
        await SeedSourceRegionMetricAndInstitutionAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/token", new
        {
            email = AdminEmail,
            password = AdminPassword,
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task PostToken_WithRepeatedInvalidAttempts_ShouldRateLimit()
    {
        await using var factory = CreateFactory();
        await SeedSourceRegionMetricAndInstitutionAsync(factory);
        using var client = factory.CreateClient();

        var rateLimited = false;
        for (var i = 0; i < 6; i++)
        {
            using var response = await client.PostAsJsonAsync("/api/v1/auth/token", new
            {
                email = AdminEmail,
                password = $"wrong-password-{i}",
            });

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rateLimited = true;
                break;
            }
        }

        Assert.True(rateLimited);
    }

    [Fact]
    public async Task PostRegionSubscription_ShouldCreateSuccessfully()
    {
        await using var factory = CreateFactory();
        await SeedSourceRegionMetricAndInstitutionAsync(factory);
        using var client = await CreateAuthenticatedClientAsync(factory);

        using var response = await client.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            source = SourceKey,
            type = "discord:webhook",
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
        Assert.Equal("https://discord.com/api/webhooks/123/***oken", created.Target);
    }

    [Fact]
    public async Task PostInstitutionSubscription_WithoutMetric_ShouldCreateWildcardSuccessfully()
    {
        await using var factory = CreateFactory();
        var seeded = await SeedSourceRegionMetricAndInstitutionAsync(factory);
        using var client = await CreateAuthenticatedClientAsync(factory);

        using var response = await client.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            source = SourceKey,
            type = "discord:webhook",
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
    public async Task PostTelegramSubscription_ShouldCreateSuccessfully()
    {
        await using var factory = CreateFactory();
        await SeedSourceRegionMetricAndInstitutionAsync(factory);
        using var client = await CreateAuthenticatedClientAsync(factory);

        using var response = await client.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            source = SourceKey,
            type = "telegram:chat",
            target = "-1001234567890",
            scopeType = "region",
            region = "pt-norte",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<SubscriptionResponse>();
        Assert.NotNull(created);
        Assert.Equal("telegram:chat", created!.Type);
        Assert.Equal("***7890", created.Target);
    }

    [Fact]
    public async Task PostSubscription_WithLegacyType_ShouldReturnBadRequest()
    {
        await using var factory = CreateFactory();
        await SeedSourceRegionMetricAndInstitutionAsync(factory);
        using var client = await CreateAuthenticatedClientAsync(factory);

        using var response = await client.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            source = SourceKey,
            type = "discord-webhook",
            target = "https://discord.com/api/webhooks/123/token",
            scopeType = "region",
            region = "pt-norte",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostTelegramSubscription_WithInvalidChatId_ShouldReturnBadRequest()
    {
        await using var factory = CreateFactory();
        await SeedSourceRegionMetricAndInstitutionAsync(factory);
        using var client = await CreateAuthenticatedClientAsync(factory);

        using var response = await client.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            source = SourceKey,
            type = "telegram:chat",
            target = "chat-id",
            scopeType = "region",
            region = "pt-norte",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains("Telegram chat id", problem!.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostSubscription_MissingScopeType_ShouldReturnBadRequest()
    {
        await using var factory = CreateFactory();
        await SeedSourceRegionMetricAndInstitutionAsync(factory);
        using var client = await CreateAuthenticatedClientAsync(factory);

        using var response = await client.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            source = SourceKey,
            type = "discord:webhook",
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
        using var client = await CreateAuthenticatedClientAsync(factory);

        using var response = await client.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            source = SourceKey,
            type = "discord:webhook",
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
        using var client = await CreateAuthenticatedClientAsync(factory);

        await client.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            source = SourceKey,
            type = "discord:webhook",
            target = "https://discord.com/api/webhooks/123/token",
            scopeType = "region",
            region = "pt-norte",
            metric = "blood-group-o-minus",
        });

        await client.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            source = SourceKey,
            type = "discord:webhook",
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
        using var client = await CreateAuthenticatedClientAsync(factory);

        using var response = await client.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            source = SourceKey,
            type = "discord:webhook",
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
        using var client = await CreateAuthenticatedClientAsync(factory);

        await client.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            source = SourceKey,
            type = "discord:webhook",
            target = "https://discord.com/api/webhooks/123/token",
            scopeType = "institution",
            institutionId = seeded.InstitutionId,
        });

        await client.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            source = SourceKey,
            type = "discord:webhook",
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
        using var client = await CreateAuthenticatedClientAsync(factory);

        using var postResponse = await client.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            source = SourceKey,
            type = "discord:webhook",
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

    [Fact]
    public async Task GetSubscriptionDeliveries_ShouldReturnLatestWithLimit()
    {
        await using var factory = CreateFactory();
        await SeedSourceRegionMetricAndInstitutionAsync(factory);
        using var client = await CreateAuthenticatedClientAsync(factory);

        using var postResponse = await client.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            source = SourceKey,
            type = "discord:webhook",
            target = "https://discord.com/api/webhooks/123/token",
            scopeType = "region",
            region = "pt-norte",
        });

        var created = await postResponse.Content.ReadFromJsonAsync<SubscriptionResponse>();
        Assert.NotNull(created);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<BloodWatchDbContext>();
            var source = await dbContext.Sources.SingleAsync(entry => entry.AdapterKey == SourceKey);
            var reserve = await dbContext.CurrentReserves.SingleAsync(entry => entry.SourceId == source.Id);

            var firstEvent = new EventEntity
            {
                Id = Guid.NewGuid(),
                SourceId = source.Id,
                CurrentReserveId = reserve.Id,
                RegionId = reserve.RegionId,
                RuleKey = "rule.v1",
                MetricKey = reserve.MetricKey,
                IdempotencyKey = Guid.NewGuid().ToString("N"),
                PayloadJson = "{}",
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-2),
            };

            var secondEvent = new EventEntity
            {
                Id = Guid.NewGuid(),
                SourceId = source.Id,
                CurrentReserveId = reserve.Id,
                RegionId = reserve.RegionId,
                RuleKey = "rule.v1",
                MetricKey = reserve.MetricKey,
                IdempotencyKey = Guid.NewGuid().ToString("N"),
                PayloadJson = "{}",
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-1),
            };

            dbContext.Events.AddRange(firstEvent, secondEvent);
            dbContext.Deliveries.AddRange(
                new DeliveryEntity
                {
                    Id = Guid.NewGuid(),
                    EventId = firstEvent.Id,
                    SubscriptionId = created!.Id,
                    AttemptCount = 3,
                    Status = "failed",
                    LastError = "test-failed",
                    CreatedAtUtc = DateTime.UtcNow.AddMinutes(-2),
                    SentAtUtc = null,
                },
                new DeliveryEntity
                {
                    Id = Guid.NewGuid(),
                    EventId = secondEvent.Id,
                    SubscriptionId = created!.Id,
                    AttemptCount = 1,
                    Status = "sent",
                    LastError = null,
                    CreatedAtUtc = DateTime.UtcNow.AddMinutes(-1),
                    SentAtUtc = DateTime.UtcNow.AddMinutes(-1),
                });

            await dbContext.SaveChangesAsync();
        }

        using var response = await client.GetAsync($"/api/v1/subscriptions/{created!.Id}/deliveries?limit=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<SubscriptionDeliveriesResponse>();
        Assert.NotNull(payload);
        Assert.Equal(created.Id, payload!.SubscriptionId);
        Assert.Single(payload.Items);
        Assert.Equal("sent", payload.Items.Single().Status);
        Assert.Equal(1, payload.Items.Single().AttemptCount);
    }

    [Fact]
    public async Task GetSubscriptions_WithMalformedBearer_ShouldReturnUnauthorized()
    {
        await using var factory = CreateFactory();
        await SeedSourceRegionMetricAndInstitutionAsync(factory);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-jwt");

        using var response = await client.GetAsync("/api/v1/subscriptions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetSubscriptions_WithExpiredBearer_ShouldReturnUnauthorized()
    {
        await using var factory = CreateFactory();
        await SeedSourceRegionMetricAndInstitutionAsync(factory);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateExpiredToken());

        using var response = await client.GetAsync("/api/v1/subscriptions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(ApiWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        using var tokenResponse = await client.PostAsJsonAsync("/api/v1/auth/token", new
        {
            email = AdminEmail,
            password = AdminPassword,
        });

        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);

        var payload = await tokenResponse.Content.ReadFromJsonAsync<CreateTokenResponse>();
        Assert.NotNull(payload);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.AccessToken);
        return client;
    }

    private static ApiWebApplicationFactory CreateFactory(IReadOnlyDictionary<string, string?>? overrides = null)
    {
        return new ApiWebApplicationFactory(overrides);
    }

    private static string CreateExpiredToken()
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSigningKey)),
            SecurityAlgorithms.HmacSha256);

        var now = DateTime.UtcNow;
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, "admin"),
            new Claim("bw:role", "admin"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };

        var token = new JwtSecurityToken(
            issuer: JwtIssuer,
            audience: JwtAudience,
            claims: claims,
            notBefore: now.AddMinutes(-20),
            expires: now.AddMinutes(-10),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
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
        private readonly IReadOnlyDictionary<string, string?> _overrides;

        public ApiWebApplicationFactory(IReadOnlyDictionary<string, string?>? overrides)
        {
            _overrides = overrides ?? new Dictionary<string, string?>();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Environment.SetEnvironmentVariable("BloodWatch__Persistence__Provider", "InMemory");
            Environment.SetEnvironmentVariable("BloodWatch__Persistence__InMemoryDatabaseName", _databaseName);

            builder.UseSetting("BloodWatch:Persistence:Provider", "InMemory");
            builder.UseSetting("BloodWatch:Persistence:InMemoryDatabaseName", _databaseName);

            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var values = new Dictionary<string, string?>
                {
                    ["BloodWatch:Persistence:Provider"] = "InMemory",
                    ["BloodWatch:Persistence:InMemoryDatabaseName"] = _databaseName,
                    ["BloodWatch:Api:Caching:LatestTtlSeconds"] = "60",
                    ["BloodWatch:Api:RateLimiting:PermitLimitPerMinute"] = "1000",
                    ["BloodWatch:Api:RateLimiting:QueueLimit"] = "0",
                    ["BloodWatch:JwtAuth:Enabled"] = "true",
                    ["BloodWatch:JwtAuth:Issuer"] = JwtIssuer,
                    ["BloodWatch:JwtAuth:Audience"] = JwtAudience,
                    ["BloodWatch:JwtAuth:SigningKey"] = JwtSigningKey,
                    ["BloodWatch:JwtAuth:AccessTokenMinutes"] = "15",
                    ["BloodWatch:JwtAuth:AdminEmail"] = AdminEmail,
                    ["BloodWatch:JwtAuth:AdminPasswordHash"] = AdminPasswordHash,
                };

                foreach (var overridePair in _overrides)
                {
                    values[overridePair.Key] = overridePair.Value;
                }

                configuration.AddInMemoryCollection(values);
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
