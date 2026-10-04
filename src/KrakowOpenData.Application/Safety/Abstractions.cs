using KrakowOpenData.Domain.Safety;

namespace KrakowOpenData.Application.Safety;

/// <summary>
/// Where citizen reports, planner alerts and agency dispatches are kept. These are the only data the system
/// writes; everything else is read-only public data. The default implementation is a JSON file so the demo
/// survives a restart; swap it for a database in production.
/// </summary>
public interface ISafetyStore
{
    Task<IReadOnlyList<CitizenReport>> ListReportsAsync(CancellationToken ct = default);

    Task<CitizenReport?> GetReportAsync(string id, CancellationToken ct = default);

    /// <summary>Inserts or replaces a report.</summary>
    Task SaveReportAsync(CitizenReport report, CancellationToken ct = default);

    Task<IReadOnlyList<PlannerAlert>> ListAlertsAsync(CancellationToken ct = default);

    Task<PlannerAlert?> GetAlertAsync(string id, CancellationToken ct = default);

    Task SaveAlertAsync(PlannerAlert alert, CancellationToken ct = default);

    Task<IReadOnlyList<AgencyDispatch>> ListDispatchesAsync(CancellationToken ct = default);

    Task AddDispatchAsync(AgencyDispatch dispatch, CancellationToken ct = default);

    /// <summary>The factor weights a planner has set (factor key → weight), or an empty map when the defaults are in use.</summary>
    Task<IReadOnlyDictionary<string, double>> GetWeightOverridesAsync(CancellationToken ct = default);

    /// <summary>Replaces the saved factor weights; an empty map goes back to the defaults.</summary>
    Task SaveWeightOverridesAsync(IReadOnlyDictionary<string, double> weights, CancellationToken ct = default);

    /// <summary>The route "good enough" thresholds a planner has set (keys like "night.average", "night.worst"), or an empty map for the defaults.</summary>
    Task<IReadOnlyDictionary<string, double>> GetRouteThresholdOverridesAsync(CancellationToken ct = default);

    /// <summary>Replaces the saved route thresholds; an empty map goes back to the defaults.</summary>
    Task SaveRouteThresholdOverridesAsync(IReadOnlyDictionary<string, double> thresholds, CancellationToken ct = default);
}

/// <summary>
/// Hands a planner's message to an external agency. The prototype implementation only records and logs it
/// ("simulated"): a real one would call the agency's ticketing API or e-mail gateway.
/// </summary>
public interface IAgencyGateway
{
    Task<AgencyDelivery> SendAsync(Agency agency, string subject, string body, CancellationToken ct = default);
}

public sealed record AgencyDelivery(string Delivery, string Reference);

/// <summary>Input was rejected; <see cref="Field"/> names the offending field.</summary>
public sealed class SafetyValidationException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

/// <summary>Too many requests from one device (reports are limited to 5 per hour).</summary>
public sealed class SafetyRateLimitException(string message) : Exception(message);
