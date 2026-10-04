namespace KrakowOpenData.Contracts;

// ── Telegram notifications ───────────────────────────────────────────────────
/// <summary>Link the app to Telegram. Latitude/Longitude are the optional "my area" point (rounded to ~100 m before storing).</summary>
public sealed record TelegramLinkRequest(string DeviceId, double? Latitude, double? Longitude);

/// <summary>A one-time deep link (valid 15 minutes) that opens the bot and links this app to the chat.</summary>
public sealed record TelegramLinkDto(string Link, DateTimeOffset ExpiresAt);

/// <summary>Available = the bot is configured on this server; Linked = this device is linked to a chat that receives messages.</summary>
public sealed record TelegramStatusDto(bool Available, bool Linked);

// ── Voice reports ────────────────────────────────────────────────────────────
/// <summary>
/// What a recording said and the AI's suggestion (unverified; the resident can change the category). The audio is not kept.
/// </summary>
public sealed record VoiceTranscriptDto(string Text, string? Language, string? SuggestedType, int? Severity, string? SummaryPl);
