namespace KrakowOpenData.Domain.Safety;

/// <summary>
/// An AI suggestion about a citizen report (Cloudflare Workers AI, see workers/ai). It is advice for a planner, never a
/// decision: it does not change the score, the report's type or its verification. <see cref="SummaryPl"/> is a short Polish
/// summary of the resident's note that the model was told to write without personal data; <see cref="ContainsPersonalData"/>
/// and <see cref="IsAbuse"/> are the model's own flags, and the summary is only shown outside the planner screens when both are false.
/// </summary>
public sealed record ReportTriage(
    string? SuggestedType,
    int Severity,
    string? Language,
    string? SummaryPl,
    bool IsAbuse,
    bool ContainsPersonalData,
    string? DuplicateOf,
    double Confidence,
    DateTimeOffset TriagedAt)
{
    /// <summary>True when the summary may be shown publicly (e.g. posted to the Telegram channel).</summary>
    public bool SummaryIsShareable => !IsAbuse && !ContainsPersonalData && !string.IsNullOrWhiteSpace(SummaryPl);
}
