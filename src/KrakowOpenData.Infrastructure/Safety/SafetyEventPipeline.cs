using System.Threading.Channels;
using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Application.Safety;
using KrakowOpenData.Domain.Safety;
using KrakowOpenData.Infrastructure.Telegram;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KrakowOpenData.Infrastructure.Safety;

/// <summary>
/// The <see cref="ISafetyEventSink"/>: a bounded in-memory queue (100 events, oldest dropped when full) and one background consumer.
/// For a new report it first asks the AI for a suggestion (<see cref="IReportTriage"/>, skipped when not configured, when the report
/// has no note or already has a suggestion) and saves it on the report, then hands every event to the <see cref="TelegramNotifier"/>.
/// A failure in one event is logged and never stops the loop or affects the request that published it.
/// </summary>
public sealed class SafetyEventPipeline(
    ISafetyStore store, IReportTriage triage, TelegramNotifier notifier, IClock clock, ILogger<SafetyEventPipeline> logger)
    : BackgroundService, ISafetyEventSink
{
    public const int Capacity = 100;
    private const double NearbyMeters = 300;

    private readonly Channel<SafetyEvent> _queue = Channel.CreateBounded<SafetyEvent>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public void Publish(SafetyEvent safetyEvent)
    {
        if (!_queue.Writer.TryWrite(safetyEvent)) logger.LogWarning("Safety event {Kind} dropped", safetyEvent.Kind);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var e in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await HandleAsync(e, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning("Safety event {Kind} failed: {Error}", e.Kind, ex.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    /// <summary>Handles one event. Public for tests.</summary>
    public async Task HandleAsync(SafetyEvent e, CancellationToken ct = default)
    {
        if (e.Kind == SafetyEventKind.AlertCreated)
        {
            if (await store.GetAlertAsync(e.Id, ct) is { } alert) notifier.AlertCreated(alert);
            return;
        }

        if (await store.GetReportAsync(e.Id, ct) is not { } report) return;
        switch (e.Kind)
        {
            case SafetyEventKind.ReportCreated:
                notifier.ReportCreated(await WithTriageAsync(report, ct));
                break;
            case SafetyEventKind.ReportMerged:
                notifier.ReportMerged(report, e.DeviceId);
                break;
            case SafetyEventKind.ReportVerified or SafetyEventKind.ReportResolved:
                notifier.StatusChanged(report, e.Kind);
                break;
        }
    }

    private async Task<CitizenReport> WithTriageAsync(CitizenReport report, CancellationToken ct)
    {
        if (report.Triage is not null || !triage.IsAvailable || string.IsNullOrWhiteSpace(report.Note)) return report;

        var since = clock.UtcNow.AddHours(-24);
        var nearby = (await store.ListReportsAsync(ct))
            .Where(r => r.Id != report.Id && r.Status == ReportStatus.Open && r.LastActivityAt >= since &&
                        GridSpec.Distance(r.Location, report.Location) <= NearbyMeters)
            .OrderByDescending(r => r.LastActivityAt)
            .Take(5)
            .Select(r => new NearbyReport(r.Id, r.Type.ToString(), r.Note))
            .ToList();

        if (await triage.TriageAsync(report.Type.ToString(), report.Note, nearby, ct) is not { } suggestion) return report;

        // Read again: a confirmation may have arrived while the model was thinking.
        var latest = await store.GetReportAsync(report.Id, ct) ?? report;
        var updated = latest with { Triage = suggestion };
        await store.SaveReportAsync(updated, ct);
        return updated;
    }
}
