using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Contracts;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Domain.Safety;

namespace KrakowOpenData.Application.Safety;

/// <summary>
/// The agencies a planner can contact from the dashboard, and the record of each contact.
///
/// <para>Phone numbers are only included where a public Kraków source states them (the 24-hour road-fault line and
/// the crisis management centre, from press coverage); those entries are marked <c>ContactVerified = false</c>
/// because the numbers were not confirmed with the agencies and must be checked before real use. Entries
/// without a number point to the agency's public page only.</para>
///
/// <para><b>Nothing is sent.</b> <see cref="IAgencyGateway"/> is simulated in this prototype: the contact is recorded,
/// a reference is issued, and the planner uses the prefilled text with the agency's own channel.</para>
/// </summary>
public sealed class AgencyService(ISafetyStore store, IAgencyGateway gateway, IClock clock)
{
    public static IReadOnlyList<Agency> Agencies { get; } =
    [
        new("zdmk", "Zarząd Dróg Miasta Krakowa (ZDMK)", "Road faults and street lighting; 24-hour dispatch", "12 616 75 55", "https://zdmk.krakow.pl", false),
        new("portal", "Kraków city services portal", "Report problems in public space (lighting failures, pavements, rubbish)", null, "https://kontakt.krakow.pl", true),
        new("crisis", "City Crisis Management Centre", "Heat waves, floods and other emergencies affecting residents", "12 616 59 99", "https://www.krakow.pl", false),
        new("straz", "Straż Miejska Kraków", "Public order, patrols, antisocial behaviour", null, "https://www.strazmiejska.krakow.pl", false),
        new("zzm", "Zarząd Zieleni Miejskiej (ZZM)", "Parks, trees and green spaces: shade and cooling", null, "https://zzm.krakow.pl", false),
        new("ztp", "Zarząd Transportu Publicznego (ZTP)", "Public transport: stops, shelters and night service", null, "https://ztp.krakow.pl", true)
    ];

    public IReadOnlyList<AgencyDto> List() => Agencies.Select(a => a.ToDto()).ToList();

    public async Task<DispatchDto> DispatchAsync(DispatchRequest request, CancellationToken ct = default)
    {
        var agency = Agencies.FirstOrDefault(a => a.Id == request.AgencyId)
            ?? throw new SafetyValidationException("agencyId", $"Use one of: {string.Join(", ", Agencies.Select(a => a.Id))}.");

        var subject = (request.Subject ?? string.Empty).Trim();
        var body = (request.Body ?? string.Empty).Trim();
        if (subject.Length is < 3 or > 120) throw new SafetyValidationException("subject", "The subject must be 3–120 characters.");
        if (body.Length is < 3 or > 2000) throw new SafetyValidationException("body", "The message must be 3–2000 characters.");

        GeoPoint? location = null;
        if (request.Latitude is { } lat && request.Longitude is { } lon)
        {
            var point = new GeoPoint(lat, lon);
            if (!GridSpec.IsInArea(point)) throw new SafetyValidationException("lat,lon", "The location must be in or around Kraków.");
            location = point;
        }

        var delivery = await gateway.SendAsync(agency, subject, body, ct);
        var dispatch = new AgencyDispatch(
            $"dis-{Guid.NewGuid():N}"[..16], agency.Id, agency.Name, subject, body, location, request.CellId, delivery.Delivery, delivery.Reference, clock.UtcNow);
        await store.AddDispatchAsync(dispatch, ct);
        return dispatch.ToDto();
    }

    public async Task<IReadOnlyList<DispatchDto>> ListDispatchesAsync(CancellationToken ct = default) =>
        (await store.ListDispatchesAsync(ct)).OrderByDescending(d => d.CreatedAt).Take(200).Select(d => d.ToDto()).ToList();
}
