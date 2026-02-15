using BloodWatch.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace BloodWatch.Adapters.Portugal.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddPortugalAdapter(this IServiceCollection services)
    {
        services.AddSingleton<IDataSourceAdapter, PortugalDataSourceAdapter>();
        return services;
    }
}
