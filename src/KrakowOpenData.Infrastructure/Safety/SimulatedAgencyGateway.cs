using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Application.Safety;
using KrakowOpenData.Domain.Safety;
using Microsoft.Extensions.Logging;

namespace KrakowOpenData.Infrastructure.Safety;

/// <summary>
/// Stand-in for a real agency integration: logs the message and returns a reference like SIM-20261003-0007.
/// Nothing leaves the system. To integrate for real, implement <see cref="IAgencyGateway"/> against the agency's
/// ticketing API or mail gateway and register it instead.
/// </summary>
public sealed class SimulatedAgencyGateway(IClock clock, ILogger<SimulatedAgencyGateway> logger) : IAgencyGateway
{
    private int _counter;

    public Task<AgencyDelivery> SendAsync(Agency agency, string subject, string body, CancellationToken ct = default)
    {
        var n = Interlocked.Increment(ref _counter);
        var reference = $"SIM-{clock.UtcNow:yyyyMMdd}-{n:0000}";
        logger.LogInformation("Simulated contact with {Agency}: {Subject} ({Reference})", agency.Name, subject, reference);
        return Task.FromResult(new AgencyDelivery("simulated", reference));
    }
}
