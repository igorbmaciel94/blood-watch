using BloodWatch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BloodWatch.Infrastructure;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddBloodWatchInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("BloodWatch")
            ?? Environment.GetEnvironmentVariable("BLOODWATCH_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=bloodwatch;Username=bloodwatch;Password=bloodwatch";

        services.AddDbContext<BloodWatchDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(BloodWatchDbContext).Assembly.FullName)));

        return services;
    }
}
