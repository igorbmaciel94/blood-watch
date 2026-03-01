using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BloodWatch.Api.Contracts;
using BloodWatch.Api.Options;
using BloodWatch.Api.Services;
using BloodWatch.Copilot;
using BloodWatch.Copilot.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BloodWatch.Api.Copilot;

public sealed class CopilotService(
    CopilotAnalyticsTools analyticsTools,
    CopilotGuardrailEvaluator guardrailEvaluator,
    CopilotIntentRouter intentRouter,
    ICopilotFeatureFlagState featureFlagState,
    ILLMClient llmClient,
    IOptions<CopilotOptions> copilotOptions,
    ILogger<CopilotService> logger,
    Infrastructure.Persistence.BloodWatchDbContext dbContext) : ICopilotService
{
    private static readonly JsonSerializerOptions PromptSerializationOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    private readonly CopilotAnalyticsTools _analyticsTools = analyticsTools;
    private readonly CopilotGuardrailEvaluator _guardrailEvaluator = guardrailEvaluator;
    private readonly CopilotIntentRouter _intentRouter = intentRouter;
    private readonly ICopilotFeatureFlagState _featureFlagState = featureFlagState;
    private readonly ILLMClient _llmClient = llmClient;
    private readonly CopilotOptions _options = copilotOptions.Value;
    private readonly ILogger<CopilotService> _logger = logger;
    private readonly Infrastructure.Persistence.BloodWatchDbContext _dbContext = dbContext;

    public async Task<ServiceResult<CopilotAnswerResponse>> AskAsync(CopilotAskRequest request, CancellationToken cancellationToken)
    {
        if (!_featureFlagState.IsEnabled)
        {
            return ServiceResult<CopilotAnswerResponse>.Failure(
                StatusCodes.Status503ServiceUnavailable,
                "Service unavailable",
                "Copilot is disabled.");
        }

        var question = Normalize(request.Question);
        if (question is null)
        {
            return ServiceResult<CopilotAnswerResponse>.Failure(
                StatusCodes.Status400BadRequest,
                "Bad request",
                "Field 'question' is required.");
        }

        var guardrailFailure = _guardrailEvaluator.Evaluate(question, _options.Guardrails);
        if (guardrailFailure is not null)
        {
            LogInteraction(
                question,
                Array.Empty<string>(),
                model: "n/a",
                tokenCountEstimate: null,
                latencyMs: 0,
                outcome: "rejected");

            return ServiceResult<CopilotAnswerResponse>.Failure(
                StatusCodes.Status400BadRequest,
                "Bad request",
                guardrailFailure);
        }

        var sourceKey = Normalize(request.Source) ?? Normalize(_options.DefaultSource)!;
        var source = await _analyticsTools.ResolveSourceAsync(sourceKey, cancellationToken);
        if (source is null)
        {
            return ServiceResult<CopilotAnswerResponse>.Failure(
                StatusCodes.Status404NotFound,
                "Not found",
                $"Source '{sourceKey}' was not found.");
        }

        var queryIds = _intentRouter.SelectQueryIds(question);
        var nowUtc = DateTime.UtcNow;

        var toolOutputs = await ExecuteQuerySetAsync(
            source,
            queryIds,
            nowUtc.AddHours(-24),
            nowUtc,
            cancellationToken);

        return await GenerateAnswerAsync(
            question,
            source.SourceKey,
            toolOutputs,
            outcomeOnFailure: "llm_unavailable",
            cancellationToken);
    }

    public async Task<ServiceResult<CopilotBriefingResponse>> GetDailyBriefingAsync(CancellationToken cancellationToken)
    {
        if (!_featureFlagState.IsEnabled)
        {
            return ServiceResult<CopilotBriefingResponse>.Failure(
                StatusCodes.Status503ServiceUnavailable,
                "Service unavailable",
                "Copilot is disabled.");
        }

        var sourceKey = Normalize(_options.DefaultSource)!;
        var source = await _analyticsTools.ResolveSourceAsync(sourceKey, cancellationToken);
        if (source is null)
        {
            return ServiceResult<CopilotBriefingResponse>.Failure(
                StatusCodes.Status404NotFound,
                "Not found",
                $"Source '{sourceKey}' was not found.");
        }

        var windowEndUtc = DateTime.UtcNow;
        var windowStartUtc = windowEndUtc.AddHours(-24);

        var toolOutputs = await ExecuteQuerySetAsync(
            source,
            CopilotConstants.AllQueryIds,
            windowStartUtc,
            windowEndUtc,
            cancellationToken);

        var answerResult = await GenerateAnswerAsync(
            $"Generate a daily operational briefing for source {source.SourceKey} in the last 24 hours.",
            source.SourceKey,
            toolOutputs,
            outcomeOnFailure: "llm_unavailable",
            cancellationToken);

        if (!answerResult.IsSuccess)
        {
            return ServiceResult<CopilotBriefingResponse>.Failure(answerResult.Error!);
        }

        return ServiceResult<CopilotBriefingResponse>.Success(new CopilotBriefingResponse(
            BriefingType: "daily",
            WindowStartUtc: windowStartUtc,
            WindowEndUtc: windowEndUtc,
            Answer: answerResult.Value!));
    }

    public async Task<ServiceResult<CopilotBriefingResponse>> GetWeeklyBriefingAsync(CancellationToken cancellationToken)
    {
        if (!_featureFlagState.IsEnabled)
        {
            return ServiceResult<CopilotBriefingResponse>.Failure(
                StatusCodes.Status503ServiceUnavailable,
                "Service unavailable",
                "Copilot is disabled.");
        }

        var sourceKey = Normalize(_options.DefaultSource)!;
        var source = await _analyticsTools.ResolveSourceAsync(sourceKey, cancellationToken);
        if (source is null)
        {
            return ServiceResult<CopilotBriefingResponse>.Failure(
                StatusCodes.Status404NotFound,
                "Not found",
                $"Source '{sourceKey}' was not found.");
        }

        var windowEndUtc = DateTime.UtcNow;
        var windowStartUtc = await ResolveWeeklyWindowStartUtcAsync(source.SourceId, windowEndUtc, cancellationToken);

        var toolOutputs = await ExecuteQuerySetAsync(
            source,
            CopilotConstants.AllQueryIds,
            windowStartUtc,
            windowEndUtc,
            cancellationToken);

        var answerResult = await GenerateAnswerAsync(
            $"Generate a weekly operational briefing for source {source.SourceKey} in the selected weekly window.",
            source.SourceKey,
            toolOutputs,
            outcomeOnFailure: "llm_unavailable",
            cancellationToken);

        if (!answerResult.IsSuccess)
        {
            return ServiceResult<CopilotBriefingResponse>.Failure(answerResult.Error!);
        }

        return ServiceResult<CopilotBriefingResponse>.Success(new CopilotBriefingResponse(
            BriefingType: "weekly",
            WindowStartUtc: windowStartUtc,
            WindowEndUtc: windowEndUtc,
            Answer: answerResult.Value!));
    }

    private async Task<DateTime> ResolveWeeklyWindowStartUtcAsync(
        Guid sourceId,
        DateTime windowEndUtc,
        CancellationToken cancellationToken)
    {
        var referenceDates = await _dbContext.ReserveHistoryObservations
            .AsNoTracking()
            .Where(entry => entry.SourceId == sourceId)
            .Select(entry => entry.ReferenceDate)
            .Distinct()
            .OrderByDescending(entry => entry)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (referenceDates.Count >= 2)
        {
            return DateTime.SpecifyKind(referenceDates[1].ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        }

        return windowEndUtc.AddDays(-7);
    }

    private async Task<IReadOnlyCollection<CopilotToolOutput>> ExecuteQuerySetAsync(
        CopilotSourceContext source,
        IReadOnlyCollection<string> queryIds,
        DateTime windowStartUtc,
        DateTime windowEndUtc,
        CancellationToken cancellationToken)
    {
        var weeks = Math.Clamp(_options.DefaultAnalyticsWeeks, 1, 104);
        var limit = Math.Clamp(_options.DefaultAnalyticsLimit, 5, 1000);

        var outputs = new List<CopilotToolOutput>();

        foreach (var queryId in queryIds.Distinct(StringComparer.Ordinal))
        {
            switch (queryId)
            {
                case CopilotConstants.CurrentCriticalQueryId:
                    outputs.Add(await _analyticsTools.GetCurrentCriticalAsync(source.SourceId, source.SourceKey, cancellationToken));
                    break;
                case CopilotConstants.WeeklyDeltaQueryId:
                    outputs.Add(await _analyticsTools.GetWeeklyDeltaAsync(source.SourceId, source.SourceKey, limit, cancellationToken));
                    break;
                case CopilotConstants.TopDowngradesQueryId:
                    outputs.Add(await _analyticsTools.GetTopDowngradesAsync(source.SourceId, source.SourceKey, weeks, limit, cancellationToken));
                    break;
                case CopilotConstants.UnstableMetricsQueryId:
                    outputs.Add(await _analyticsTools.GetUnstableMetricsAsync(source.SourceId, source.SourceKey, weeks, limit, cancellationToken));
                    break;
                case CopilotConstants.FailedDeliveriesQueryId:
                    outputs.Add(await _analyticsTools.GetFailedDeliveriesAsync(source.SourceId, source.SourceKey, windowStartUtc, windowEndUtc, limit, cancellationToken));
                    break;
                case CopilotConstants.FailingSubscriptionTypesQueryId:
                    outputs.Add(await _analyticsTools.GetFailingSubscriptionTypesAsync(source.SourceId, source.SourceKey, windowStartUtc, windowEndUtc, limit, cancellationToken));
                    break;
            }
        }

        return outputs;
    }

    private async Task<ServiceResult<CopilotAnswerResponse>> GenerateAnswerAsync(
        string requestDescription,
        string sourceKey,
        IReadOnlyCollection<CopilotToolOutput> toolOutputs,
        string outcomeOnFailure,
        CancellationToken cancellationToken)
    {
        var startedAtUtc = DateTime.UtcNow;
        var requestHash = ComputeSha256Hex(requestDescription);
        var queryIdsUsed = toolOutputs.Select(output => output.QueryId).Distinct(StringComparer.Ordinal).ToArray();

        var systemPrompt = """
You are BloodWatch Copilot.
Domain rules:
- BloodWatch is informational monitoring only.
- Never provide medical advice.
- Severity order is: critical > warning > watch > normal > unknown.
- Only use the provided tool outputs as evidence.
- If data is insufficient, say that clearly.
Return valid JSON only with this schema:
{
  "shortAnswer": "string",
  "summaryBullets": ["string", "string"]
}
""";

        var payload = new
        {
            source = sourceKey,
            request = requestDescription,
            toolOutputs = toolOutputs.Select(output => new
            {
                output.QueryId,
                output.Description,
                entries = output.Entries.Select(entry => new
                {
                    entry.ResultId,
                    entry.Data,
                }),
            }),
        };

        var userPrompt = JsonSerializer.Serialize(payload, PromptSerializationOptions);

        try
        {
            var llmResult = await _llmClient.GenerateAsync(
                userPrompt,
                new LLMGenerateOptions(
                    SystemPrompt: systemPrompt,
                    Temperature: 0.2,
                    MaxTokens: 500),
                cancellationToken);

            var parsed = CopilotResponseParser.Parse(llmResult.Text);
            var dataBasis = toolOutputs
                .Select(output => new CopilotDataBasisItem(output.QueryId, output.Description))
                .ToArray();

            var citations = toolOutputs
                .Select(output => new CopilotCitation(
                    output.QueryId,
                    output.Entries.Select(entry => entry.ResultId).Take(50).ToArray()))
                .ToArray();

            var answer = new CopilotAnswerResponse(
                ShortAnswer: parsed.ShortAnswer,
                SummaryBullets: parsed.SummaryBullets,
                DataBasis: dataBasis,
                Citations: citations,
                Disclaimer: CopilotConstants.Disclaimer,
                GeneratedAtUtc: DateTime.UtcNow,
                Model: llmResult.Model);

            var latencyMs = (long)(DateTime.UtcNow - startedAtUtc).TotalMilliseconds;
            _logger.LogInformation(
                "Copilot interaction completed. timestampUtc={TimestampUtc} requestHashSha256={RequestHashSha256} queryIdsUsed={QueryIdsUsed} model={Model} tokenCountEstimate={TokenCountEstimate} latencyMs={LatencyMs} outcome={Outcome}",
                DateTime.UtcNow,
                requestHash,
                string.Join(',', queryIdsUsed),
                llmResult.Model,
                llmResult.TotalTokens,
                latencyMs,
                "success");

            return ServiceResult<CopilotAnswerResponse>.Success(answer);
        }
        catch (LLMClientException ex)
        {
            var latencyMs = (long)(DateTime.UtcNow - startedAtUtc).TotalMilliseconds;
            _logger.LogWarning(
                ex,
                "Copilot interaction failed. timestampUtc={TimestampUtc} requestHashSha256={RequestHashSha256} queryIdsUsed={QueryIdsUsed} model={Model} tokenCountEstimate={TokenCountEstimate} latencyMs={LatencyMs} outcome={Outcome}",
                DateTime.UtcNow,
                requestHash,
                string.Join(',', queryIdsUsed),
                "n/a",
                null,
                latencyMs,
                outcomeOnFailure);

            return ServiceResult<CopilotAnswerResponse>.Failure(
                StatusCodes.Status503ServiceUnavailable,
                "Service unavailable",
                "Copilot is currently unavailable. Please retry later.");
        }
    }

    private void LogInteraction(
        string request,
        IReadOnlyCollection<string> queryIdsUsed,
        string model,
        int? tokenCountEstimate,
        long latencyMs,
        string outcome)
    {
        _logger.LogInformation(
            "Copilot interaction completed. timestampUtc={TimestampUtc} requestHashSha256={RequestHashSha256} queryIdsUsed={QueryIdsUsed} model={Model} tokenCountEstimate={TokenCountEstimate} latencyMs={LatencyMs} outcome={Outcome}",
            DateTime.UtcNow,
            ComputeSha256Hex(request),
            string.Join(',', queryIdsUsed),
            model,
            tokenCountEstimate,
            latencyMs,
            outcome);
    }

    private static string ComputeSha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}
