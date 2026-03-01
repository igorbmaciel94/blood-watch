using System.Text.RegularExpressions;
using BloodWatch.Api.Options;

namespace BloodWatch.Api.Copilot;

public sealed class CopilotGuardrailEvaluator
{
    private static readonly Regex SecretRequestRegex = new(
        "(token|secret|password|credential|api[-_ ]?key|signing key|webhook)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex WriteActionRegex = new(
        "\\b(create|delete|disable|enable|update|insert|drop|truncate|grant|revoke|alter|remove)\\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex SexualMinorContentRegex = new(
        "(?=.*\\b(minor|child|children|underage|teen|kid)\\b)(?=.*\\b(sex|sexual|explicit|porn|erotic|nude|nsfw)\\b)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ViolentInstructionRegex = new(
        "(?=.*\\b(how to|step[- ]?by[- ]?step|instructions?|guide|teach|walk me through)\\b)(?=.*\\b(kill|murder|hurt|harm|assault|stab|shoot|bomb|poison|attack)\\b)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex SelfHarmEncouragementRegex = new(
        "(?=.*\\b(self[- ]?harm|suicide)\\b)(?=.*\\b(encourage\\w*|persuad\\w*|persuasive|instructions?|guide|how to|step[- ]?by[- ]?step)\\b)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex IgnoreRulesRegex = new(
        "(ignore|disregard).{0,40}(all|previous|prior).{0,40}(rules?|instructions?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex DeveloperModeRegex = new(
        "(developer mode|unfilteredgpt|jailbreak|do anything now|\\bdan\\b)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex DecodeAndObeyRegex = new(
        "(decode.{0,24}base64|follow it exactly|obey exactly|execute this instruction)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public string? Evaluate(string question, CopilotGuardrailsOptions options)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return "Field 'question' is required.";
        }

        var normalized = question.Trim();
        var maxLength = Math.Clamp(options.MaxQuestionLength, 100, 10_000);
        if (normalized.Length > maxLength)
        {
            return $"Field 'question' exceeds maximum length ({maxLength}).";
        }

        if (SecretRequestRegex.IsMatch(normalized))
        {
            return "Request rejected by security guardrails: secrets and credentials are not accessible.";
        }

        if (WriteActionRegex.IsMatch(normalized))
        {
            return "Request rejected by security guardrails: Copilot is read-only and cannot perform write actions.";
        }

        if (IgnoreRulesRegex.IsMatch(normalized)
            || DeveloperModeRegex.IsMatch(normalized)
            || DecodeAndObeyRegex.IsMatch(normalized))
        {
            return "Request rejected by security guardrails: prompt-injection attempts are not allowed.";
        }

        if (SexualMinorContentRegex.IsMatch(normalized))
        {
            return "Request rejected by safety guardrails: sexual content involving minors is strictly prohibited.";
        }

        if (ViolentInstructionRegex.IsMatch(normalized) || SelfHarmEncouragementRegex.IsMatch(normalized))
        {
            return "Request rejected by safety guardrails: harmful instructions are not allowed.";
        }

        return null;
    }
}
