using BloodWatch.Adapters.Portugal.DependencyInjection;
using BloodWatch.Api;
using BloodWatch.Api.DependencyInjection;
using BloodWatch.Api.Endpoints;
using BloodWatch.Api.Middleware;
using BloodWatch.Api.Options;
using BloodWatch.Infrastructure;
using BloodWatch.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

if (TryHandlePasswordHashCommand(args))
{
    return;
}

if (await TryHandleMigrationCommandAsync(args))
{
    return;
}

var builder = WebApplication.CreateBuilder(args);
ConfigureStructuredLogging(builder);

builder.Services
    .AddBloodWatchInfrastructure(builder.Configuration)
    .AddPortugalAdapter()
    .AddBloodWatchApi(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseRateLimiter();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();

app.MapGet("/", () => Results.Ok(new
{
    service = "BloodWatch.Api",
    status = "ok",
    mode = "http",
    disclaimer = "No medical advice",
    openApiSpec = "/openapi/v1.json",
    docs = "/docs",
})).ExcludeFromDescription();

app.MapGet("/docs", () => Results.Content(
    SwaggerUiHtml.Build("BloodWatch API", "/openapi/v1.json"),
    "text/html"))
    .ExcludeFromDescription();

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }))
    .ExcludeFromDescription();

app.MapGet("/health/ready", CheckReadinessAsync)
    .ExcludeFromDescription();

app.MapGet("/health", CheckReadinessAsync)
    .ExcludeFromDescription();

app.MapGet("/version", (IOptions<BuildInfoOptions> buildInfoAccessor) =>
    Results.Ok(BuildVersionPayload(buildInfoAccessor.Value)))
    .ExcludeFromDescription();

app.MapPublicReadEndpoints();
app.MapSubscriptionAuthEndpoints();
app.MapSubscriptionEndpoints();
app.MapFallbackToFile("/app", "app/index.html");
app.MapFallbackToFile("/app/{*path:nonfile}", "app/index.html");

await app.RunAsync();

static bool TryHandlePasswordHashCommand(string[] args)
{
    if (args.Length == 0 || !string.Equals(args[0], "hash-password", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
    {
        Console.Error.WriteLine("Usage: dotnet run --project src/BloodWatch.Api -- hash-password \"<plain-password>\"");
        Environment.ExitCode = 1;
        return true;
    }

    var hasher = new PasswordHasher<string>();
    var passwordHash = hasher.HashPassword("bloodwatch-admin", args[1]);
    Console.WriteLine(passwordHash);
    return true;
}

static void ConfigureStructuredLogging(WebApplicationBuilder builder)
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(options =>
    {
        options.UseUtcTimestamp = true;
        options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        options.IncludeScopes = true;
    });
}

static async Task<bool> TryHandleMigrationCommandAsync(string[] args)
{
    if (args.Length == 0 || !string.Equals(args[0], "migrate", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    var migrationBuilder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = args.Skip(1).ToArray(),
    });
    ConfigureStructuredLogging(migrationBuilder);

    migrationBuilder.Services
        .AddBloodWatchInfrastructure(migrationBuilder.Configuration);

    var app = migrationBuilder.Build();
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Migrator");
    await EnsureDatabaseReadyAsync(app.Services, logger);
    logger.LogInformation("Database migration command completed.");
    return true;
}

static async Task EnsureDatabaseReadyAsync(IServiceProvider services, ILogger logger)
{
    using var scope = services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<BloodWatchDbContext>();

    if (!dbContext.Database.IsRelational())
    {
        await dbContext.Database.EnsureCreatedAsync();
        logger.LogInformation("Database provider is non-relational. EnsureCreated completed.");
        return;
    }

    await services.ApplyMigrationsWithRetryAsync(logger);
}

static async Task<IResult> CheckReadinessAsync(BloodWatchDbContext dbContext, CancellationToken cancellationToken)
{
    var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
    return canConnect
        ? Results.Ok(new { status = "ready" })
        : Results.Problem(
            title: "Database unavailable",
            detail: "The API could not connect to its database.",
            statusCode: StatusCodes.Status503ServiceUnavailable,
            type: "https://httpstatuses.com/503");
}

static object BuildVersionPayload(BuildInfoOptions buildInfo)
{
    return new
    {
        version = NormalizeBuildValue(buildInfo.Version),
        commit = NormalizeBuildValue(buildInfo.Commit),
        buildDate = NormalizeBuildValue(buildInfo.Date),
    };
}

static string NormalizeBuildValue(string? value)
{
    return string.IsNullOrWhiteSpace(value)
        ? "unknown"
        : value.Trim();
}

public partial class Program;
