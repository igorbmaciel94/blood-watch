namespace BloodWatch.Api.Contracts;

public sealed record CopilotAskRequest(string? Question, string? Source);

public sealed record CopilotDataBasisItem(string QueryId, string Description);

public sealed record CopilotCitation(string QueryId, IReadOnlyCollection<string> ResultIds);

public sealed record CopilotAnswerResponse(
    string ShortAnswer,
    IReadOnlyCollection<string> SummaryBullets,
    IReadOnlyCollection<CopilotDataBasisItem> DataBasis,
    IReadOnlyCollection<CopilotCitation> Citations,
    string Disclaimer,
    DateTime GeneratedAtUtc,
    string Model);

public sealed record CopilotBriefingResponse(
    string BriefingType,
    DateTime WindowStartUtc,
    DateTime WindowEndUtc,
    CopilotAnswerResponse Answer);
