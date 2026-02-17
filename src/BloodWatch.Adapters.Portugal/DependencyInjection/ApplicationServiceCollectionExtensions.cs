using BloodWatch.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BloodWatch.Adapters.Portugal.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddPortugalAdapter(this IServiceCollection services)
    {
        services
            .AddOptions<TransparenciaSnsClientOptions>()
            .BindConfiguration(TransparenciaSnsClientOptions.SectionName);

        services.AddHttpClient<ITransparenciaSnsClient, TransparenciaSnsClient>((serviceProvider, httpClient) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<TransparenciaSnsClientOptions>>().Value;
            var userAgent = string.IsNullOrWhiteSpace(options.UserAgent)
                ? "BloodWatch/0.1 (+https://github.com/igorbmaciel/blood-watch)"
                : options.UserAgent;

            httpClient.BaseAddress = new Uri(options.BaseUrl);
            httpClient.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 5, 120));
            httpClient.DefaultRequestHeaders.UserAgent.Clear();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        });

        services.AddSingleton<PortugalReservasMapper>();
        services.AddSingleton<IDataSourceAdapter, PortugalAdapter>();

        return services;
    }
}
