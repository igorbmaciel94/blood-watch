using System.Text.Json;

namespace BloodWatch.Adapters.Portugal;

public sealed class DadorSessionsMapper
{
    public IReadOnlyCollection<DadorSessionRecord> Map(JsonElement payloadRoot)
    {
        if (!TryResolveRows(payloadRoot, out var rows))
        {
            return [];
        }

        var records = new List<DadorSessionRecord>();
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var externalId = DadorParsingHelpers.ReadString(row, "Id");
            var institutionCode = DadorParsingHelpers.ReadString(row, "SiglaInstituicao");
            var institutionName = DadorParsingHelpers.ReadString(row, "DesInstituicao");
            if (externalId is null || institutionCode is null || institutionName is null)
            {
                continue;
            }

            var rawRegionName = DadorParsingHelpers.ReadString(row, "DesNuts");
            var region = DadorParsingHelpers.NormalizeRegion(rawRegionName);
            var (latitude, longitude) = DadorParsingHelpers.ParseGeoReference(
                DadorParsingHelpers.ReadString(row, "GeoReferencia"));

            DateOnly? sessionDate = null;
            if (DadorParsingHelpers.TryParseDateOnly(DadorParsingHelpers.ReadString(row, "DataBrigada"), out var parsedDate))
            {
                sessionDate = parsedDate;
            }

            records.Add(new DadorSessionRecord(
                ExternalId: externalId,
                InstitutionCode: institutionCode,
                InstitutionName: institutionName,
                RegionKey: region.Key,
                RegionName: region.DisplayName,
                DistrictCode: DadorParsingHelpers.ReadString(row, "CodDistrito"),
                DistrictName: DadorParsingHelpers.ReadString(row, "DesDistrito"),
                MunicipalityCode: DadorParsingHelpers.ReadString(row, "CodConcelho"),
                MunicipalityName: DadorParsingHelpers.ReadString(row, "DesConcelho"),
                Location: DadorParsingHelpers.ReadString(row, "Local"),
                Latitude: latitude,
                Longitude: longitude,
                SessionDate: sessionDate,
                SessionHours: DadorParsingHelpers.ReadString(row, "HoraBrigada"),
                AccessCode: DadorParsingHelpers.ReadString(row, "Acesso"),
                StateCode: DadorParsingHelpers.ReadString(row, "Estado"),
                SessionTypeCode: DadorParsingHelpers.ReadString(row, "CodTipoSessao"),
                SessionTypeName: DadorParsingHelpers.ReadString(row, "DesTipoSessao")));
        }

        return records
            .GroupBy(record => record.ExternalId, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(record => record.SessionDate)
            .ThenBy(record => record.InstitutionName, StringComparer.Ordinal)
            .ThenBy(record => record.ExternalId, StringComparer.Ordinal)
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

        return DadorParsingHelpers.TryGetPropertyIgnoreCase(dataNode, "Sessoes", out rows)
               && rows.ValueKind == JsonValueKind.Array;
    }
}
