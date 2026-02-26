using BloodWatch.Core.Models;

namespace BloodWatch.Core.Tests;

public sealed class ReserveStatusCatalogTests
{
    [Theory]
    [InlineData("normal", 0)]
    [InlineData("watch", 1)]
    [InlineData("warning", 2)]
    [InlineData("critical", 3)]
    [InlineData("unknown", -1)]
    [InlineData("invalid", -1)]
    [InlineData(null, -1)]
    public void GetRank_ShouldUseCanonicalSeverityScale(string? statusKey, int expectedRank)
    {
        var rank = ReserveStatusCatalog.GetRank(statusKey);
        Assert.Equal(expectedRank, rank);
    }
}
