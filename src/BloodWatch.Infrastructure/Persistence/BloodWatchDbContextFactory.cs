using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BloodWatch.Infrastructure.Persistence;

public sealed class BloodWatchDbContextFactory : IDesignTimeDbContextFactory<BloodWatchDbContext>
{
    public BloodWatchDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<BloodWatchDbContext>();
        var connectionString = Environment.GetEnvironmentVariable("BLOODWATCH_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=bloodwatch;Username=bloodwatch;Password=bloodwatch";

        optionsBuilder.UseNpgsql(connectionString);

        return new BloodWatchDbContext(optionsBuilder.Options);
    }
}
