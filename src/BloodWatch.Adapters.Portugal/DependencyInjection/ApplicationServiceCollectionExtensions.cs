using BloodWatch.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BloodWatch.Adapters.Portugal.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddPortugalAdapter(this IServiceCollection services)
    {
        services
            .AddOptions<DadorPtClientOptions>()
            .BindConfiguration(DadorPtClientOptions.SectionName);

        services.AddHttpClient<IDadorPtClient, DadorPtClient>((serviceProvider, httpClient) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<DadorPtClientOptions>>().Value;
            var userAgent = string.IsNullOrWhiteSpace(options.UserAgent)
                ? "BloodWatch/0.1 (+https://github.com/igorbmaciel/blood-watch)"
                : options.UserAgent;

            httpClient.BaseAddress = new Uri(options.BaseUrl);
            httpClient.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 5, 120));
            httpClient.DefaultRequestHeaders.UserAgent.Clear();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        });

        services.AddSingleton<PortugalReservasMapper>();
        services.AddSingleton<DadorInstitutionsMapper>();
        services.AddSingleton<DadorSessionsMapper>();
        services.AddSingleton<IDataSourceAdapter, PortugalAdapter>();

        return services;
    }
}
