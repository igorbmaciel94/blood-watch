using BloodWatch.Adapters.Portugal.DependencyInjection;
using BloodWatch.Api;
using BloodWatch.Api.DependencyInjection;
using BloodWatch.Infrastructure;
using BloodWatch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddBloodWatchInfrastructure(builder.Configuration)
    .AddPortugalAdapter()
    .AddBloodWatchApi();

var app = builder.Build();

var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
await app.Services.ApplyMigrationsWithRetryAsync(startupLogger);

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
        : Results.Problem("Database unavailable", statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapGet("/api/v1/sources", async (BloodWatchDbContext dbContext, CancellationToken cancellationToken) =>
{
    var sources = await dbContext.Sources
        .AsNoTracking()
        .OrderBy(source => source.Name)
        .Select(source => new
        {
            source.Id,
            source.AdapterKey,
            source.Name,
        })
        .ToListAsync(cancellationToken);

    return Results.Ok(sources);
}).ExcludeFromDescription();

await app.RunAsync();
