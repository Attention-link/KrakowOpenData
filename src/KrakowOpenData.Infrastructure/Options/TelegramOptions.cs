namespace KrakowOpenData.Infrastructure.Options;

/// <summary>
/// Configuration section "Telegram": the notification bot (ported from SafeWalk Kraków). Everything is off unless
/// <see cref="BotToken"/> is set, so tests and local runs send nothing. See README, "AI (Cloudflare Workers AI) i Telegram".
/// </summary>
public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    /// <summary>Bot token from @BotFather. A secret: set it only through the environment (Telegram__BotToken).</summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>The bot's @username without the "@", used for the t.me deep link that links a resident's app to their chat.</summary>
    public string BotUsername { get; set; } = string.Empty;

    /// <summary>Optional staff chat (a group or channel id such as -1001234567890, or "@channel") that gets a digest of every new report.</summary>
    public string StaffChatId { get; set; } = string.Empty;

    /// <summary>
    /// Run the getUpdates long-polling loop. Only ONE poller per token may run anywhere (Telegram answers 409 to a second one),
    /// so turn this off while another stack (e.g. SafeWalk) still polls the same token. Null = on when a token is set.
    /// Sending (outbox, staff digest) works either way.
    /// </summary>
    public bool? Polling { get; set; }

    /// <summary>The public site, linked from messages (e.g. https://opendata.al.mt). Empty = no link.</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>Telegram Bot API address; only changed in tests.</summary>
    public string ApiBaseUrl { get; set; } = "https://api.telegram.org";

    /// <summary>How long a link code from the app stays valid.</summary>
    public int LinkTtlMinutes { get; set; } = 15;

    /// <summary>File for links, pending codes, the outbox and the update offset. Empty = telegram-store.json next to the safety store.</summary>
    public string StorePath { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BotToken);

    public bool PollingEnabled => IsConfigured && (Polling ?? true);

    public bool CanLink => IsConfigured && !string.IsNullOrWhiteSpace(BotUsername);
}
