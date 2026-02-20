using System.Text.Json;

namespace BloodWatch.Adapters.Portugal;

public sealed class DadorInstitutionsMapper
{
    public IReadOnlyCollection<DadorInstitutionRecord> Map(JsonElement payloadRoot)
    {
        if (!TryResolveRows(payloadRoot, out var rows))
        {
            return [];
        }

        var records = new List<DadorInstitutionRecord>();
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var externalId = DadorParsingHelpers.ReadString(row, "Id");
            var institutionCode = DadorParsingHelpers.ReadString(row, "SiglaInstituicao");
            var institutionName = DadorParsingHelpers.ReadString(row, "DesInstituicao");
            var rawRegionName = DadorParsingHelpers.ReadString(row, "DesNuts");

            if (externalId is null || institutionCode is null || institutionName is null)
            {
                continue;
            }

            var region = DadorParsingHelpers.NormalizeRegion(rawRegionName);
            var (latitude, longitude) = DadorParsingHelpers.ParseGeoReference(
                DadorParsingHelpers.ReadString(row, "GeoReferencia"));

            records.Add(new DadorInstitutionRecord(
                ExternalId: externalId,
                InstitutionCode: institutionCode,
                Name: institutionName,
                RegionKey: region.Key,
                RegionName: region.DisplayName,
                DistrictCode: DadorParsingHelpers.ReadString(row, "CodDistrito"),
                DistrictName: DadorParsingHelpers.ReadString(row, "DesDistrito"),
                MunicipalityCode: DadorParsingHelpers.ReadString(row, "CodConcelho"),
                MunicipalityName: DadorParsingHelpers.ReadString(row, "DesConcelho"),
                Address: DadorParsingHelpers.ReadString(row, "Morada"),
                Latitude: latitude,
                Longitude: longitude,
                PlusCode: DadorParsingHelpers.ReadString(row, "GeoReferenciaPlus"),
                Schedule: DadorParsingHelpers.ReadString(row, "Horario"),
                Phone: DadorParsingHelpers.ReadString(row, "Telefone"),
                MobilePhone: DadorParsingHelpers.ReadString(row, "Telemovel"),
                Email: DadorParsingHelpers.ReadString(row, "Email")));
        }

        return records
            .GroupBy(record => record.ExternalId, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(record => record.RegionKey, StringComparer.Ordinal)
            .ThenBy(record => record.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool TryResolveRows(JsonElement payloadRoot, out JsonElement rows)
    {
        rows = default;

        if (!DadorParsingHelpers.TryGetPropertyIgnoreCase(payloadRoot, "data", out var dataNode)
            || dataNode.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return DadorParsingHelpers.TryGetPropertyIgnoreCase(dataNode, "CentrosColheita", out rows)
               && rows.ValueKind == JsonValueKind.Array;
    }
}
