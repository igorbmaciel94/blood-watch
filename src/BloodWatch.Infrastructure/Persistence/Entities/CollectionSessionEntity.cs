namespace BloodWatch.Infrastructure.Persistence.Entities;

public sealed class CollectionSessionEntity
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public Guid RegionId { get; set; }
    public Guid? DonationCenterId { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string InstitutionCode { get; set; } = string.Empty;
    public string InstitutionName { get; set; } = string.Empty;
    public string? DistrictCode { get; set; }
    public string? DistrictName { get; set; }
    public string? MunicipalityCode { get; set; }
    public string? MunicipalityName { get; set; }
    public string? Location { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public DateOnly? SessionDate { get; set; }
    public string? SessionHours { get; set; }
    public string? AccessCode { get; set; }
    public string? StateCode { get; set; }
    public string? SessionTypeCode { get; set; }
    public string? SessionTypeName { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public SourceEntity Source { get; set; } = null!;
    public RegionEntity Region { get; set; } = null!;
    public DonationCenterEntity? DonationCenter { get; set; }
}
