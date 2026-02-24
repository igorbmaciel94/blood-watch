using BloodWatch.Api.Options;
using BloodWatch.Worker.Options;
using Microsoft.Extensions.Hosting;

namespace BloodWatch.Core.Tests;

public sealed class ProductionRuntimeOptionsValidatorTests
{
    [Fact]
    public void ApiValidator_InProduction_WithMissingValues_ShouldFail()
    {
        var validator = new BloodWatch.Api.Options.ProductionRuntimeOptionsValidator(
            new FakeHostEnvironment(Environments.Production));

        var result = validator.Validate(
            name: null,
            new BloodWatch.Api.Options.ProductionRuntimeOptions
            {
                ConnectionString = "",
                JwtSigningKey = "short",
                JwtAdminEmail = "",
                JwtAdminPasswordHash = "",
                BuildVersion = "",
                BuildCommit = "",
                BuildDate = "not-a-date",
            });

        Assert.NotNull(result.FailureMessage);
        Assert.Contains("ConnectionStrings__BloodWatch", result.FailureMessage!, StringComparison.Ordinal);
        Assert.Contains("BloodWatch__JwtAuth__SigningKey", result.FailureMessage!, StringComparison.Ordinal);
        Assert.Contains("BloodWatch__Build__Date", result.FailureMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiValidator_InProduction_WithValidValues_ShouldSucceed()
    {
        var validator = new BloodWatch.Api.Options.ProductionRuntimeOptionsValidator(
            new FakeHostEnvironment(Environments.Production));

        var result = validator.Validate(
            name: null,
            new BloodWatch.Api.Options.ProductionRuntimeOptions
            {
                ConnectionString = "Host=postgres;Port=5432;Database=bloodwatch;Username=bloodwatch;Password=secret",
                JwtSigningKey = new string('s', 64),
                JwtAdminEmail = "admin@example.com",
                JwtAdminPasswordHash = "AQAAAAIAAYagAAAAEFakeHash",
                BuildVersion = "v1.2.3",
                BuildCommit = "abc1234",
                BuildDate = "2026-02-24T00:00:00Z",
            });

        Assert.Null(result.FailureMessage);
    }

    [Fact]
    public void WorkerValidator_InProduction_WithMissingValues_ShouldFail()
    {
        var validator = new BloodWatch.Worker.Options.ProductionRuntimeOptionsValidator(
            new FakeHostEnvironment(Environments.Production));

        var result = validator.Validate(
            name: null,
            new BloodWatch.Worker.Options.ProductionRuntimeOptions
            {
                ConnectionString = "",
                BuildVersion = "",
                BuildCommit = "",
                BuildDate = "bad-date",
            });

        Assert.NotNull(result.FailureMessage);
        Assert.Contains("ConnectionStrings__BloodWatch", result.FailureMessage!, StringComparison.Ordinal);
        Assert.Contains("BloodWatch__Build__Version", result.FailureMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkerValidator_InDevelopment_AllowsMissingValues()
    {
        var validator = new BloodWatch.Worker.Options.ProductionRuntimeOptionsValidator(
            new FakeHostEnvironment(Environments.Development));

        var result = validator.Validate(
            name: null,
            new BloodWatch.Worker.Options.ProductionRuntimeOptions());

        Assert.Null(result.FailureMessage);
    }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "BloodWatch.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
