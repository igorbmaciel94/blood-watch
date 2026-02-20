namespace BloodWatch.Infrastructure.Persistence.Entities;

public sealed class DonationCenterEntity
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public Guid RegionId { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string InstitutionCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? DistrictCode { get; set; }
    public string? DistrictName { get; set; }
    public string? MunicipalityCode { get; set; }
    public string? MunicipalityName { get; set; }
    public string? Address { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string? PlusCode { get; set; }
    public string? Schedule { get; set; }
    public string? Phone { get; set; }
    public string? MobilePhone { get; set; }
    public string? Email { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public SourceEntity Source { get; set; } = null!;
    public RegionEntity Region { get; set; } = null!;
    public ICollection<CollectionSessionEntity> CollectionSessions { get; set; } = [];
    public ICollection<SubscriptionEntity> Subscriptions { get; set; } = [];
}
