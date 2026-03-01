namespace BloodWatch.Api.Copilot;

public sealed record CopilotToolEntry(
    string ResultId,
    IReadOnlyDictionary<string, object?> Data);

public sealed record CopilotToolOutput(
    string QueryId,
    string Description,
    IReadOnlyCollection<CopilotToolEntry> Entries);

public sealed record CopilotSourceContext(Guid SourceId, string SourceKey);
