namespace KrakowOpenData.Infrastructure.Options;

/// <summary>
/// Configuration section "Ai": the Cloudflare Worker in workers/ai that runs Workers AI (report triage and voice transcription).
/// Off unless <see cref="BaseUrl"/> is set; without it reports are simply not triaged and voice input is unavailable.
/// See README, "AI (Cloudflare Workers AI) i Telegram".
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>
    /// The Worker's base address: the zone route (https://opendata.al.mt/ai) or its workers.dev URL
    /// (https://krakow-ai.&lt;account&gt;.workers.dev/ai). The API calls {BaseUrl}/triage and {BaseUrl}/transcribe.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Shared secret sent as X-Ai-Key; must match the Worker's AI_KEY secret. Set it only through the environment (Ai__Key).</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>How long to wait for triage before giving up (the report then goes on without a suggestion).</summary>
    public int TimeoutSeconds { get; set; } = 6;

    /// <summary>How long to wait for a voice transcription (up to 60 s of audio).</summary>
    public int TranscribeTimeoutSeconds { get; set; } = 25;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);
}
