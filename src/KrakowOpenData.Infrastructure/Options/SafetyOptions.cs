namespace KrakowOpenData.Infrastructure.Options;

/// <summary>Configuration section "Safety" in appsettings.json.</summary>
public sealed class SafetyOptions
{
    public const string SectionName = "Safety";

    /// <summary>
    /// Key the planner dashboard sends in the <c>X-Planner-Key</c> header. DEMO ONLY: a shared key is not real
    /// authentication. Replace it with the city's identity provider before any real use, and change this value.
    /// </summary>
    public string PlannerKey { get; set; } = "demo-planner";

    /// <summary>Save reports, alerts and contacts to a file so they survive a restart.</summary>
    public bool Persist { get; set; } = true;

    /// <summary>File to save to. Empty = %LOCALAPPDATA%/KrakowOpenData/safety-store.json.</summary>
    public string StorePath { get; set; } = string.Empty;

    /// <summary>
    /// POST/PUT/DELETE requests under /api/safety allowed per client IP per minute (fixed window). 0 or less turns the
    /// limit off. It sits on top of the 5-reports-an-hour limit per device, which a script can dodge with new device ids.
    /// The default is generous on purpose: at an event hundreds of phones share one public (NAT) address, so this is only a
    /// flood guard; the per-device limits (reports, voice notes) are the real spam guard. Planner calls with a valid key and
    /// the read-only <c>POST /api/safety/access/route</c> are not counted (see SafetyRateLimit).
    /// </summary>
    public int WriteRequestsPerMinute { get; set; } = 120;
}
