using System.Text.Json;
using BloodWatch.Adapters.Portugal;

namespace BloodWatch.Core.Tests;

public sealed class PortugalReservasMapperTests
{
    private readonly PortugalReservasMapper _mapper = new();

    [Fact]
    public void Map_ShouldParseDadorStructureIncludingRegionalBlocks()
    {
        const string json = """
        {
          "success": true,
          "data": {
            "ReservasSangue": {
              "Data": [["16/02/2026"]],
              "IPST": [
                {"ABO":"A +","Cor":"VERDE"},
                {"ABO":"O -","Cor":"LARANJA"}
              ],
              "NACIONAL": [
                {"ABO":"A +","Cor":"VERMELHO"}
              ],
              "Regiao": [
                ["NORTE"],
                {"ABO":"A +","Cor":"AMARELO"},
                {"ABO":"O -","Cor":"LARANJA"},
                ["ALGARVE"],
                {"ABO":"O -","Cor":"VERDE"}
              ]
            },
            "version": "16/02/2026 11:00"
          }
        }
        """;

        using var document = JsonDocument.Parse(json);
        var snapshot = _mapper.Map(document.RootElement, new DateTime(2026, 2, 20, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal("pt-dador-ipst", snapshot.Source.AdapterKey);
        Assert.Equal(new DateOnly(2026, 2, 16), snapshot.ReferenceDate);
        Assert.Equal(new DateTime(2026, 2, 16, 11, 0, 0, DateTimeKind.Utc), snapshot.SourceUpdatedAtUtc);

        Assert.Contains(snapshot.Items, item =>
            item.Region.Key == "pt-ipst"
            && item.Metric.Key == "blood-group-o-minus"
            && item.StatusKey == "warning");

        Assert.Contains(snapshot.Items, item =>
            item.Region.Key == "pt-nacional"
            && item.Metric.Key == "blood-group-a-plus"
            && item.StatusKey == "critical");

        Assert.Contains(snapshot.Items, item =>
            item.Region.Key == "pt-norte"
            && item.Metric.Key == "blood-group-a-plus"
            && item.StatusKey == "watch");

        Assert.Contains(snapshot.Items, item =>
            item.Region.Key == "pt-algarve"
            && item.Metric.Key == "blood-group-o-minus"
            && item.StatusKey == "normal");
    }

    [Fact]
    public void Map_ShouldMapUnknownColorToUnknownStatus()
    {
        const string json = """
        {
          "success": true,
          "data": {
            "ReservasSangue": {
              "Data": [["16/02/2026"]],
              "IPST": [
                {"ABO":"A +","Cor":"AZUL"}
              ]
            },
            "version": "16/02/2026 11:00"
          }
        }
        """;

        using var document = JsonDocument.Parse(json);
        var snapshot = _mapper.Map(document.RootElement, new DateTime(2026, 2, 20, 12, 0, 0, DateTimeKind.Utc));

        var item = Assert.Single(snapshot.Items);
        Assert.Equal("unknown", item.StatusKey);
        Assert.Equal("Unknown", item.StatusLabel);
    }
}
