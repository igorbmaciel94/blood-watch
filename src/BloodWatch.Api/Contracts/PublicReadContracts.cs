namespace BloodWatch.Api.Contracts;

public sealed record SourceQuery(string? Source);
public sealed record LatestReservesQuery(string? Source, string? Region, string? Metric);

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
    decimal Value,
    string Unit,
    string? Severity);
