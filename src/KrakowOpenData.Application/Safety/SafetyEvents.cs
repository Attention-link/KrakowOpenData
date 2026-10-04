using KrakowOpenData.Contracts;
using KrakowOpenData.Domain.Safety;

namespace KrakowOpenData.Application.Safety;

public enum SafetyEventKind
{
    /// <summary>A resident filed a NEW report (not a repeat merged into an open one).</summary>
    ReportCreated,

    /// <summary>A repeat of an open report was merged into it as a confirmation; <see cref="SafetyEvent.DeviceId"/> is who sent it.</summary>
    ReportMerged,

    /// <summary>A planner verified a report.</summary>
    ReportVerified,

    /// <summary>A planner resolved (fixed or dismissed) a report.</summary>
    ReportResolved,

    /// <summary>A planner created an alert.</summary>
    AlertCreated
}

/// <summary>
/// Something happened that other systems may want to hear about (AI triage, Telegram notifications). Only ids travel: the consumer
/// reads the record from <see cref="ISafetyStore"/>. The queue is in memory only.
/// </summary>
public sealed record SafetyEvent(SafetyEventKind Kind, string Id, string? DeviceId = null);

/// <summary>
/// Fire-and-forget hand-off of <see cref="SafetyEvent"/>s. Publishing never blocks and never fails the request that caused it:
/// when the queue is full the oldest event is dropped. Demo data is never published.
/// </summary>
public interface ISafetyEventSink
{
    void Publish(SafetyEvent safetyEvent);
}

public static class SafetyEventSinkExtensions
{
    /// <summary>
    /// Publishes what <see cref="ReportService.CreateAsync"/> did: a NEW report (one supporter, never touched since it was created)
    /// or a repeat merged into an open one.
    /// </summary>
    public static void ReportFiled(this ISafetyEventSink sink, ReportDto created, string? deviceId) =>
        sink.Publish(created.Supporters == 1 && created.CreatedAt == created.LastActivityAt
            ? new SafetyEvent(SafetyEventKind.ReportCreated, created.Id)
            : new SafetyEvent(SafetyEventKind.ReportMerged, created.Id, deviceId));
}

/// <summary>An open report near a new one, given to the AI so it can spot duplicates.</summary>
public sealed record NearbyReport(string Id, string Type, string? Note);

/// <summary>
/// Asks an AI model for a suggestion about a report text (category, severity 1–3, a short Polish summary, abuse and personal-data
/// flags, a likely duplicate). Returns null when AI is not configured or does not answer in time; callers must work the same
/// without it. The result is advice for planners, never a decision.
/// </summary>
public interface IReportTriage
{
    bool IsAvailable { get; }

    Task<ReportTriage?> TriageAsync(string type, string? note, IReadOnlyList<NearbyReport> nearby, CancellationToken ct = default);
}

/// <summary>What a voice message said, with the AI suggestion for it. The audio itself is never kept.</summary>
public sealed record VoiceTranscript(string Text, string? Language, ReportTriage? Triage);

/// <summary>Speech to text for residents who cannot type easily (Workers AI Whisper). Null when unavailable or on failure.</summary>
public interface IVoiceTranscriber
{
    bool IsAvailable { get; }

    Task<VoiceTranscript?> TranscribeAsync(byte[] audio, string contentType, CancellationToken ct = default);
}
