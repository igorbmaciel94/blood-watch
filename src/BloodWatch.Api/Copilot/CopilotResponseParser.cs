using System.Text.Json;

namespace BloodWatch.Api.Copilot;

public static class CopilotResponseParser
{
    private const string DefaultShortAnswer = "Copilot could not produce a valid short answer.";

    public static (string ShortAnswer, IReadOnlyCollection<string> SummaryBullets) Parse(string rawModelOutput)
    {
        if (TryParseJson(rawModelOutput, out var shortAnswer, out var summaryBullets))
        {
            return (EnsureShortAnswer(shortAnswer), summaryBullets);
        }

        var normalized = string.IsNullOrWhiteSpace(rawModelOutput)
            ? "No answer generated."
            : rawModelOutput.Trim();

        var firstSentence = normalized
            .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        return (EnsureShortAnswer(firstSentence ?? normalized), Array.Empty<string>());
    }

    private static bool TryParseJson(
        string rawModelOutput,
        out string shortAnswer,
        out IReadOnlyCollection<string> summaryBullets)
    {
        shortAnswer = DefaultShortAnswer;
        summaryBullets = Array.Empty<string>();

        if (string.IsNullOrWhiteSpace(rawModelOutput))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(rawModelOutput);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            string? parsedShortAnswer = null;
            if (root.TryGetProperty("shortAnswer", out var shortAnswerProperty)
                && shortAnswerProperty.ValueKind == JsonValueKind.String)
            {
                parsedShortAnswer = shortAnswerProperty.GetString();
            }

            var bullets = new List<string>();
            if (root.TryGetProperty("summaryBullets", out var bulletsProperty)
                && bulletsProperty.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in bulletsProperty.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var bullet = item.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(bullet))
                    {
                        bullets.Add(bullet);
                    }
                }
            }

            shortAnswer = EnsureShortAnswer(parsedShortAnswer);
            summaryBullets = bullets;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string EnsureShortAnswer(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return DefaultShortAnswer;
        }

        if (normalized is "{" or "}" or "[" or "]")
        {
            return DefaultShortAnswer;
        }

        return normalized.Any(char.IsLetterOrDigit)
            ? normalized
            : DefaultShortAnswer;
    }
}
