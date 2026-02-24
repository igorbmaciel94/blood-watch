using BloodWatch.Adapters.Portugal.DependencyInjection;
using BloodWatch.Core.Contracts;
using BloodWatch.Infrastructure;
using BloodWatch.Infrastructure.Persistence;
using BloodWatch.Worker.Options;
using BloodWatch.Worker;
using BloodWatch.Worker.Dispatch;
using BloodWatch.Worker.Notifiers;
using BloodWatch.Worker.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
ConfigureStructuredLogging(builder);

builder.Services
    .AddBloodWatchInfrastructure(builder.Configuration)
    .AddPortugalAdapter();

builder.Services
    .AddOptions<BuildInfoOptions>()
    .BindConfiguration(BuildInfoOptions.SectionName);

builder.Services
    .AddOptions<ProductionRuntimeOptions>()
    .Configure<IConfiguration>((options, config) =>
    {
        options.ConnectionString = config.GetConnectionString("BloodWatch")
                                   ?? config["BLOODWATCH_CONNECTION_STRING"];
        options.BuildVersion = config["BloodWatch:Build:Version"];
        options.BuildCommit = config["BloodWatch:Build:Commit"];
        options.BuildDate = config["BloodWatch:Build:Date"];
    })
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<ProductionRuntimeOptions>, ProductionRuntimeOptionsValidator>();

builder.Services
    .Configure<FetchPortugalReservesOptions>(builder.Configuration.GetSection(FetchPortugalReservesOptions.SectionName))
    .AddSingleton<IRule, StatusTransitionRule>()
    .AddScoped<DispatchEngine>()
    .AddScoped<FetchPortugalReservesJob>();

builder.Services
    .AddHttpClient<DiscordWebhookNotifier>((serviceProvider, httpClient) =>
    {
        httpClient.Timeout = ResolveTimeout(
            serviceProvider,
            "BLOODWATCH:DISCORD_WEBHOOK_TIMEOUT_SECONDS",
            "BLOODWATCH__DISCORD_WEBHOOK_TIMEOUT_SECONDS");
    });

builder.Services
    .AddHttpClient<TelegramNotifier>((serviceProvider, httpClient) =>
    {
        httpClient.Timeout = ResolveTimeout(
            serviceProvider,
            "BLOODWATCH:TELEGRAM_TIMEOUT_SECONDS",
            "BLOODWATCH__TELEGRAM_TIMEOUT_SECONDS");
    });

builder.Services.AddTransient<INotifier>(serviceProvider => serviceProvider.GetRequiredService<DiscordWebhookNotifier>());
builder.Services.AddTransient<INotifier>(serviceProvider => serviceProvider.GetRequiredService<TelegramNotifier>());

var ingestionEnabled = builder.Configuration.GetValue("BloodWatch:Worker:Ingestion:Enabled", true);
if (ingestionEnabled)
{
    builder.Services.AddHostedService<IngestionWorker>();
}

builder.Services.AddOpenApi();

var app = builder.Build();

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

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }))
    .ExcludeFromDescription();

app.MapGet("/health/ready", async (BloodWatchDbContext dbContext, CancellationToken cancellationToken) =>
{
    var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
    return canConnect
        ? Results.Ok(new { status = "ready" })
        : Results.Problem("Database unavailable", statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapGet("/health", async (BloodWatchDbContext dbContext, CancellationToken cancellationToken) =>
{
    var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
    return canConnect
        ? Results.Ok(new { status = "ready" })
        : Results.Problem("Database unavailable", statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapGet("/version", (IOptions<BuildInfoOptions> buildInfoAccessor) =>
    Results.Ok(new
    {
        version = NormalizeBuildValue(buildInfoAccessor.Value.Version),
        commit = NormalizeBuildValue(buildInfoAccessor.Value.Commit),
        buildDate = NormalizeBuildValue(buildInfoAccessor.Value.Date),
    }))
    .ExcludeFromDescription();

await app.RunAsync();

static TimeSpan ResolveTimeout(IServiceProvider serviceProvider, string configKey, string envVarKey)
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var timeoutRaw = configuration[configKey] ?? Environment.GetEnvironmentVariable(envVarKey);
    var timeoutSeconds = int.TryParse(timeoutRaw, out var parsedValue) ? parsedValue : 10;
    return TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 5, 120));
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

static string NormalizeBuildValue(string? value)
{
    return string.IsNullOrWhiteSpace(value)
        ? "unknown"
        : value.Trim();
}
