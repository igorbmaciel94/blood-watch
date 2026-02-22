namespace BloodWatch.Core.Models;

public static class NotificationChannelTypeCatalog
{
    public const string DiscordWebhook = "discord:webhook";
    public const string TelegramChat = "telegram:chat";

    public const string LegacyDiscordWebhook = "discord-webhook";
    public const string LegacyTelegramChat = "telegram-chat";

    public static bool IsCanonical(string? rawValue)
    {
        return string.Equals(rawValue, DiscordWebhook, StringComparison.Ordinal)
               || string.Equals(rawValue, TelegramChat, StringComparison.Ordinal);
    }

    public static bool TryNormalizeStored(string? rawValue, out string normalizedValue)
    {
        normalizedValue = string.Empty;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var value = rawValue.Trim();
        if (string.Equals(value, DiscordWebhook, StringComparison.Ordinal)
            || string.Equals(value, LegacyDiscordWebhook, StringComparison.Ordinal))
        {
            normalizedValue = DiscordWebhook;
            return true;
        }

        if (string.Equals(value, TelegramChat, StringComparison.Ordinal)
            || string.Equals(value, LegacyTelegramChat, StringComparison.Ordinal))
        {
            normalizedValue = TelegramChat;
            return true;
        }

        return false;
    }
}
