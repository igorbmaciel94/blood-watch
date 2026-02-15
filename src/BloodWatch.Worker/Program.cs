using BloodWatch.Adapters.Portugal.DependencyInjection;
using BloodWatch.Infrastructure;
using BloodWatch.Infrastructure.Persistence;
using BloodWatch.Worker;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddBloodWatchInfrastructure(builder.Configuration)
    .AddPortugalAdapter();

builder.Services.AddHostedService<IngestionWorker>();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
await app.Services.ApplyMigrationsWithRetryAsync(startupLogger);

app.MapGet("/", () => Results.Ok(new
{
    service = "BloodWatch.Worker",
    status = "ok",
    mode = "background",
    openApiSpec = "/openapi/v1.json",
    docs = "/docs",
})).ExcludeFromDescription();

app.MapOpenApi();

app.MapGet("/docs", () => Results.Content(
    SwaggerUiHtml.Build("BloodWatch Worker", "/openapi/v1.json"),
    "text/html"))
    .ExcludeFromDescription();

app.MapHealthChecks("/health").ExcludeFromDescription();

app.MapGet("/health/ready", async (BloodWatchDbContext dbContext, CancellationToken cancellationToken) =>
{
    var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
    return canConnect
        ? Results.Ok(new { status = "ready" })
        : Results.Problem("Database unavailable", statusCode: StatusCodes.Status503ServiceUnavailable);
});

await app.RunAsync();
