using System.Collections.Concurrent;
using KrakowOpenData.Application.Safety;

namespace KrakowOpenData.Api.Tests.Fakes;

/// <summary>Replaces the real event pipeline in API tests: records what the endpoints publish, sends nothing.</summary>
public sealed class CapturingSafetyEventSink : ISafetyEventSink
{
    public ConcurrentQueue<SafetyEvent> Events { get; } = new();

    public void Publish(SafetyEvent safetyEvent) => Events.Enqueue(safetyEvent);
}
