namespace BloodWatch.Api.Contracts;

public sealed record SourceQuery(string? Source);
public sealed record LatestReservesQuery(string? Source, string? Region, string? Metric);
public sealed record InstitutionsQuery(string? Source, string? Region);
public sealed record NearestInstitutionsQuery(string? Source, decimal? Lat, decimal? Lon, int? Limit);
public sealed record SessionsQuery(string? Source, string? Region, string? FromDate, int? Limit);
public sealed record ReserveDeltasQuery(string? Source, int? Limit);
public sealed record TopDowngradesQuery(string? Source, int? Weeks, int? Limit);
public sealed record TimeInStatusQuery(string? Source, int? Weeks, int? Limit);
public sealed record UnstableMetricsQuery(string? Source, int? Weeks, int? Limit);

public sealed record SourcesResponse(IReadOnlyCollection<SourceItem> Items);

public sealed record SourceItem(string Source, string Name);

public sealed record RegionsResponse(string Source, IReadOnlyCollection<RegionItem> Items);

public sealed record RegionItem(string Key, string Name);

public sealed record LatestReservesResponse(
    string Source,
    DateTime CapturedAtUtc,
    DateOnly? ReferenceDate,
    IReadOnlyCollection<LatestReservesItem> Items);

public sealed record LatestReservesItem(
    RegionItem Region,
    string Metric,
    string StatusKey,
    string StatusLabel);

public sealed record InstitutionsResponse(string Source, IReadOnlyCollection<InstitutionItem> Items);

public sealed record InstitutionItem(
    Guid Id,
    string ExternalId,
    string Code,
    string Name,
    RegionItem Region,
    string? District,
    string? Municipality,
    string? Address,
    decimal? Latitude,
    decimal? Longitude,
    string? Schedule,
    string? Phone,
    string? Email);

public sealed record NearestInstitutionsResponse(
    string Source,
    decimal Latitude,
    decimal Longitude,
    IReadOnlyCollection<NearestInstitutionItem> Items);

public sealed record NearestInstitutionItem(
    InstitutionItem Institution,
    double DistanceKm);

public sealed record SessionsResponse(string Source, DateOnly FromDate, IReadOnlyCollection<SessionItem> Items);

public sealed record SessionItem(
    Guid Id,
    string ExternalId,
    DateOnly? Date,
    string? Hours,
    string? SessionType,
    string? State,
    string? Location,
    RegionItem Region,
    SessionInstitutionItem Institution,
    decimal? Latitude,
    decimal? Longitude);

public sealed record SessionInstitutionItem(Guid? Id, string Code, string Name);

public sealed record ReserveDeltasResponse(
    string Source,
    DateOnly CurrentReferenceDate,
    DateOnly PreviousReferenceDate,
    IReadOnlyCollection<ReserveDeltaItem> Items);

public sealed record ReserveDeltaItem(
    RegionItem Region,
    string Metric,
    string PreviousStatusKey,
    short PreviousStatusRank,
    string CurrentStatusKey,
    short CurrentStatusRank,
    short RankDelta);

public sealed record TopDowngradesResponse(
    string Source,
    int Weeks,
    DateOnly FromReferenceDate,
    DateOnly ToReferenceDate,
    IReadOnlyCollection<TopDowngradeItem> Items);

public sealed record TopDowngradeItem(
    RegionItem Region,
    int Downgrades);

public sealed record TimeInStatusResponse(
    string Source,
    int Weeks,
    DateOnly FromReferenceDate,
    DateOnly ToReferenceDate,
    IReadOnlyCollection<TimeInStatusItem> Items);

public sealed record TimeInStatusItem(
    RegionItem Region,
    string Metric,
    int WatchWeeks,
    int WarningWeeks,
    int CriticalWeeks,
    int TotalObservedWeeks);

public sealed record UnstableMetricsResponse(
    string Source,
    int Weeks,
    DateOnly FromReferenceDate,
    DateOnly ToReferenceDate,
    IReadOnlyCollection<UnstableMetricItem> Items);

public sealed record UnstableMetricItem(
    RegionItem Region,
    string Metric,
    int Transitions);
