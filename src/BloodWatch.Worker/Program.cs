using BloodWatch.Adapters.Portugal.DependencyInjection;
using BloodWatch.Core.Contracts;
using BloodWatch.Infrastructure;
using BloodWatch.Infrastructure.Persistence;
using BloodWatch.Worker;
using BloodWatch.Worker.Dispatch;
using BloodWatch.Worker.Notifiers;
using BloodWatch.Worker.Rules;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddBloodWatchInfrastructure(builder.Configuration)
    .AddPortugalAdapter();

builder.Services
    .Configure<FetchPortugalReservesOptions>(builder.Configuration.GetSection(FetchPortugalReservesOptions.SectionName))
    .AddSingleton<IRule, StatusTransitionRule>()
    .AddScoped<DispatchEngine>()
    .AddScoped<FetchPortugalReservesJob>();

builder.Services
    .AddHttpClient<DiscordWebhookNotifier>((serviceProvider, httpClient) =>
    {
        var configuration = serviceProvider.GetRequiredService<IConfiguration>();
        var timeoutRaw = configuration["BLOODWATCH:DISCORD_WEBHOOK_TIMEOUT_SECONDS"]
            ?? Environment.GetEnvironmentVariable("BLOODWATCH__DISCORD_WEBHOOK_TIMEOUT_SECONDS");
        var timeoutSeconds = int.TryParse(timeoutRaw, out var parsedValue) ? parsedValue : 10;

        httpClient.Timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 5, 120));
    });

builder.Services.AddTransient<INotifier>(serviceProvider => serviceProvider.GetRequiredService<DiscordWebhookNotifier>());

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
