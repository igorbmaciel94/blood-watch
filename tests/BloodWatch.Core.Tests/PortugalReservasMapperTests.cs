using System.Text.Json;
using BloodWatch.Adapters.Portugal;

namespace BloodWatch.Core.Tests;

public sealed class PortugalReservasMapperTests
{
    private readonly PortugalReservasMapper _mapper = new();

    [Fact]
    public void Map_ShouldConvertSamplePayloadToCanonicalSnapshot()
    {
        const string json = """
        {
          "results": [
            {
              "periodo": "2026-01",
              "regiao": "Regiao de Saude Norte",
              "grupo_sanguineo": "Total",
              "reservas": 100
            },
            {
              "periodo": "2026-01",
              "regiao": "Regiao de Saude Norte",
              "grupo_sanguineo": "A+",
              "reservas": "25,5"
            },
            {
              "periodo": "2025-12",
              "regiao": "Regiao de Saude LVT",
              "grupo_sanguineo": "Total",
              "reservas": 999
            }
          ]
        }
        """;

        using var document = JsonDocument.Parse(json);
        var snapshot = _mapper.Map(document.RootElement, new DateTime(2026, 2, 17, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal("pt-transparencia-sns", snapshot.Source.AdapterKey);
        Assert.Equal(new DateOnly(2026, 1, 1), snapshot.ReferenceDate);
        Assert.Equal(2, snapshot.Items.Count);

        Assert.Contains(snapshot.Items, item =>
            item.Region.Key == "pt-norte"
            && item.Metric.Key == "overall"
            && item.Value == 100m);

        Assert.Contains(snapshot.Items, item =>
            item.Region.Key == "pt-norte"
            && item.Metric.Key == "blood-group-a-plus"
            && item.Value == 25.5m);
    }

    [Fact]
    public void Map_ShouldTolerateUnknownFieldsAndNulls()
    {
        const string json = """
        [
          {
            "fields": {
              "periodo": "2026-02",
              "regiao": "Regiao de Saude do Algarve",
              "grupo_sanguineo": "Total",
              "reservas": "321,7",
              "extra_field": "ignored"
            },
            "something_new": {
              "nested": true
            }
          },
          {
            "fields": {
              "periodo": "2026-02",
              "regiao": "Regiao de Saude do Algarve",
              "grupo_sanguineo": "Total",
              "reservas": null
            }
          },
          {
            "fields": {
              "periodo": "2026-01",
              "regiao": "Regiao de Saude do Algarve",
              "grupo_sanguineo": "Total",
              "reservas": 200
            }
          }
        ]
        """;

        using var document = JsonDocument.Parse(json);
        var snapshot = _mapper.Map(document.RootElement, new DateTime(2026, 2, 17, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateOnly(2026, 2, 1), snapshot.ReferenceDate);

        var item = Assert.Single(snapshot.Items);
        Assert.Equal("pt-algarve", item.Region.Key);
        Assert.Equal("overall", item.Metric.Key);
        Assert.Equal(321.7m, item.Value);
    }
}
