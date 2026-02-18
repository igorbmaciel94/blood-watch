using BloodWatch.Adapters.Portugal.DependencyInjection;
using BloodWatch.Api;
using BloodWatch.Api.DependencyInjection;
using BloodWatch.Api.Endpoints;
using BloodWatch.Infrastructure;
using BloodWatch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddBloodWatchInfrastructure(builder.Configuration)
    .AddPortugalAdapter()
    .AddBloodWatchApi();

var app = builder.Build();

await EnsureDatabaseReadyAsync(app);

app.UseExceptionHandler();
app.UseRateLimiter();

app.MapOpenApi();

app.MapGet("/", () => Results.Ok(new
{
    service = "BloodWatch.Api",
    status = "ok",
    disclaimer = "No medical advice",
    openApiSpec = "/openapi/v1.json",
    docs = "/docs",
})).ExcludeFromDescription();

app.MapGet("/docs", () => Results.Content(
    SwaggerUiHtml.Build("BloodWatch API", "/openapi/v1.json"),
    "text/html"))
    .ExcludeFromDescription();

app.MapGet("/health", async (BloodWatchDbContext dbContext, CancellationToken cancellationToken) =>
{
    var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
    return canConnect
        ? Results.Ok(new { status = "healthy" })
        : Results.Problem(
            title: "Database unavailable",
            detail: "The API could not connect to its database.",
            statusCode: StatusCodes.Status503ServiceUnavailable,
            type: "https://httpstatuses.com/503");
});

app.MapPublicReadEndpoints();

await app.RunAsync();

static async Task EnsureDatabaseReadyAsync(WebApplication app)
{
    var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<BloodWatchDbContext>();
    if (!dbContext.Database.IsRelational())
    {
        await dbContext.Database.EnsureCreatedAsync();
        startupLogger.LogInformation("Database provider is non-relational. EnsureCreated completed.");
        return;
    }

    await app.Services.ApplyMigrationsWithRetryAsync(startupLogger);
}

public partial class Program;
