namespace BloodWatch.Core.Models;

public static class ReserveStatusCatalog
{
    public const string Critical = "critical";
    public const string Warning = "warning";
    public const string Watch = "watch";
    public const string Normal = "normal";
    public const string Unknown = "unknown";

    public static string NormalizeKey(string? rawStatusKey)
    {
        if (string.IsNullOrWhiteSpace(rawStatusKey))
        {
            return Unknown;
        }

        var normalized = rawStatusKey.Trim().ToLowerInvariant();
        return normalized switch
        {
            Critical => Critical,
            Warning => Warning,
            Watch => Watch,
            Normal => Normal,
            Unknown => Unknown,
            _ => Unknown,
        };
    }

    public static string GetLabel(string? rawStatusKey)
    {
        return NormalizeKey(rawStatusKey) switch
        {
            Critical => "Critical",
            Warning => "Warning",
            Watch => "Watch",
            Normal => "Normal",
            _ => "Unknown",
        };
    }

    public static int GetRank(string? rawStatusKey)
    {
        return NormalizeKey(rawStatusKey) switch
        {
            Critical => 3,
            Warning => 2,
            Watch => 1,
            Normal => 0,
            _ => -1,
        };
    }

    public static bool IsNormal(string? rawStatusKey)
    {
        return NormalizeKey(rawStatusKey) == Normal;
    }
}
