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
}
