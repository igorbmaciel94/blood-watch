using Microsoft.Extensions.DependencyInjection;

namespace BloodWatch.Api.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddBloodWatchApi(this IServiceCollection services)
    {
        services.AddOpenApi();
        return services;
    }
}
