using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Domain.Safety;

/// <summary>An external agency a planner can contact.</summary>
public sealed record Agency(
    string Id,
    string Name,
    string Responsibility,
    string? Phone,
    string? Url,
    bool ContactVerified);

/// <summary>
/// A record of a planner contacting an agency. In this prototype nothing leaves the system:
/// <see cref="Delivery"/> is "simulated" and the prefilled message and contact details are for the planner
/// to use through the agency's own channel.
/// </summary>
public sealed record AgencyDispatch(
    string Id,
    string AgencyId,
    string AgencyName,
    string Subject,
    string Body,
    GeoPoint? Location,
    string? CellId,
    string Delivery,
    string Reference,
    DateTimeOffset CreatedAt) : IEntity;
