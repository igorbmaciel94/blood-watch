using BloodWatch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BloodWatch.Infrastructure;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddBloodWatchInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["BloodWatch:Persistence:Provider"];
        if (string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            var databaseName = configuration["BloodWatch:Persistence:InMemoryDatabaseName"] ?? "bloodwatch-dev";

            services.AddDbContext<BloodWatchDbContext>(options =>
                options.UseInMemoryDatabase(databaseName));

            return services;
        }

        var connectionString = configuration.GetConnectionString("BloodWatch")
            ?? Environment.GetEnvironmentVariable("BLOODWATCH_CONNECTION_STRING");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Missing BloodWatch database connection string. Configure ConnectionStrings__BloodWatch (or BLOODWATCH_CONNECTION_STRING).");
        }

        services.AddDbContext<BloodWatchDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(BloodWatchDbContext).Assembly.FullName)));

        return services;
    }
}
