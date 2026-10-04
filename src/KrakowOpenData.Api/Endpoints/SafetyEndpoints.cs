using System.Security.Cryptography;
using System.Text;
using KrakowOpenData.Application.Safety;
using KrakowOpenData.Domain.Safety;
using KrakowOpenData.Contracts;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace KrakowOpenData.Api.Endpoints;

/// <summary>
/// "Safety concerns": heat and night-safety scores for any place in Kraków, citizen reports, and planner tools
/// (dashboard, alerts, contacting agencies). Public endpoints need no key. Planner endpoints need the
/// <c>X-Planner-Key</c> header (demo-grade protection, see <see cref="SafetyOptions.PlannerKey"/>).
/// How scores are computed: <see cref="SafetyModel"/>.
/// </summary>
public static class SafetyEndpoints
{
    public const string PlannerKeyHeader = "X-Planner-Key";

    public static RouteGroupBuilder MapSafetyEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/safety").WithTags("Safety").AddEndpointFilter<SafetyErrorsFilter>();
        g.MapNotificationEndpoints(); // Telegram link / unlink and voice notes (NotificationEndpoints.cs)

        // ── Public: scores and conditions ────────────────────────────────────
        g.MapGet("/conditions", async (ConditionsService svc, CancellationToken ct) => Results.Ok(await svc.GetAsync(ct)))
            .WithName("GetSafetyConditions")
            .WithSummary("Live conditions: heat pressure, air quality, river alerts, warnings, and whether it is dark.")
            .Produces<ConditionsDto>();

        g.MapGet("/grid", async (string? @event, ScoreService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetGridAsync(ScoreService.ParseEvent(@event), ct)))
            .WithName("GetSafetyGrid")
            .WithSummary("The whole 250 m score grid in compact form (heat, safety, combined, exposure, reports, priority). event = heat | night | flood | air (heat is the heat-relief score).")
            .Produces<GridDto>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        g.MapGet("/place", async (double lat, double lon, string? @event, ScoreService svc, CancellationToken ct) =>
        {
            var point = new GeoPoint(lat, lon);
            if (!GridSpec.IsInArea(point)) return Results.ValidationProblem(Problem("lat,lon", "The location must be in or around Kraków."));
            return Results.Ok(await svc.GetPlaceAsync(point, ScoreService.ParseEvent(@event), ct));
        })
            .WithName("GetSafetyPlace")
            .WithSummary("Heat-relief, night-safety, flood and air score of any point: factors, nearest relief and safe places, nearby reports, suggested actions.")
            .Produces<PlaceScoreDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        g.MapGet("/cells/{id}", async (string id, string? @event, HttpContext http, ScoreService svc, IOptions<SafetyOptions> options, CancellationToken ct) =>
            await svc.GetCellAsync(id, ScoreService.ParseEvent(@event), IsPlanner(http, options.Value), ct) is { } place ? Results.Ok(place) : Results.NotFound())
            .WithName("GetSafetyCell")
            .WithSummary("One grid cell by id (row-col). Planners (with the key) also see report notes.")
            .Produces<PlaceScoreDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        g.MapGet("/features", async (ScoreService svc, CancellationToken ct) => Results.Ok(await svc.GetFeaturesAsync(ct)))
            .WithName("GetSafetyFeatures")
            .WithSummary("Water points, toilets, parks, indoor refuges, night-open places and AEDs behind the scores, for offline use.")
            .Produces<IReadOnlyList<FeatureDto>>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        g.MapGet("/corridor", async (string from, string to, ScoreService svc, CancellationToken ct) =>
        {
            if (!GeoPoint.TryParse(from, out var a) || !GeoPoint.TryParse(to, out var b) || !GridSpec.IsInArea(a) || !GridSpec.IsInArea(b))
                return Results.ValidationProblem(Problem("from,to", "Give from and to as \"lat,lon\" inside Kraków."));
            return Results.Ok(await svc.GetCorridorAsync(a, b, ct));
        })
            .WithName("GetSafetyCorridor")
            .WithSummary("Scores sampled every 50 m along the straight line between two points (a night-walk check, not a street route).")
            .Produces<CorridorDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        g.MapGet("/route", async (string from, string to, string? mode, RouteService svc, CancellationToken ct) =>
        {
            if (!GeoPoint.TryParse(from, out var a) || !GeoPoint.TryParse(to, out var b) || !GridSpec.IsInArea(a) || !GridSpec.IsInArea(b))
                return Results.ValidationProblem(Problem("from,to", "Give from and to as \"lat,lon\" inside Kraków."));
            return Results.Ok(await svc.GetRoutesAsync(a, b, ScoreService.ParseEvent(mode), ct));
        })
            .WithName("GetSafetyRoute")
            .WithSummary("Walking routes along real streets: the fastest, and a safer (night) or cooler (heat) alternative when one scores clearly better. mode = night | heat | both.")
            .Produces<RoutesDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        g.MapGet("/method", async (MethodService svc, CancellationToken ct) => Results.Ok(await svc.GetAsync(ct)))
            .WithName("GetSafetyMethod")
            .WithSummary("How every score is built: meaning of 0 and 100, bands, each factor with weight, thresholds, why it is weighted so, data source and mapped count, plus definitions of the dashboard statistics.")
            .Produces<MethodDto>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // ── Public: accessibility (Kraków bez barier) — see AccessEndpoints ──
        g.MapAccessEndpoints();

        // ── Public: citizen reports and alerts ───────────────────────────────
        g.MapGet("/report-types", () => Results.Ok(ReportService.Types))
            .WithName("GetReportTypes")
            .WithSummary("What residents can report, which score each type feeds, and how long it counts.")
            .Produces<IReadOnlyList<ReportTypeDto>>();

        g.MapGet("/reports", async (double? lat, double? lon, double? radius, bool? includeResolved, int? limit,
            string? layer, string? type, string? status, bool? verified, string? q,
            HttpContext http, ReportService svc, IOptions<SafetyOptions> options, CancellationToken ct) =>
        {
            ScoreLayer? parsedLayer = null;
            if (!string.IsNullOrWhiteSpace(layer))
            {
                if (!SafetyEnum.TryParseName<ScoreLayer>(layer, out var l))
                    return Results.ValidationProblem(Problem("layer", $"Use one of: {string.Join(", ", Enum.GetNames<ScoreLayer>())}."));
                parsedLayer = l;
            }

            ReportType? parsedType = null;
            if (!string.IsNullOrWhiteSpace(type))
            {
                if (!SafetyEnum.TryParseName<ReportType>(type, out var t))
                    return Results.ValidationProblem(Problem("type", $"Use one of: {string.Join(", ", Enum.GetNames<ReportType>())}."));
                parsedType = t;
            }

            var parsedStatus = includeResolved == true ? ReportStatusFilter.All : ReportStatusFilter.Open;
            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!SafetyEnum.TryParseName<ReportStatusFilter>(status, out var st))
                    return Results.ValidationProblem(Problem("status", $"Use one of: {string.Join(", ", Enum.GetNames<ReportStatusFilter>())}."));
                parsedStatus = st;
            }

            GeoPoint? near = null;
            if (lat is not null || lon is not null)
            {
                var candidate = new GeoPoint(lat ?? double.NaN, lon ?? double.NaN);
                if (lat is null || lon is null || !candidate.IsValid) return Results.ValidationProblem(Problem("lat,lon", "Give both lat and lon as valid coordinates."));
                near = candidate;
            }

            // Residents only ever see open reports, and never notes; planners may ask for resolved ones and search the notes (free text never looks at a note the caller cannot read).
            var planner = IsPlanner(http, options.Value);
            var filter = new ReportFilter(near, radius ?? 1000, planner ? parsedStatus : ReportStatusFilter.Open, parsedLayer, parsedType, verified, q);
            return Results.Ok(await svc.ListAsync(filter, planner, limit ?? 300, ct));
        })
            .WithName("GetReports")
            .WithSummary("Open citizen reports (optionally near a point). Notes are visible to planners only.")
            .Produces<IReadOnlyList<ReportDto>>()
            .ProducesValidationProblem();

        g.MapPost("/reports", async (CreateReportRequest request, ReportService svc, ISafetyEventSink events, CancellationToken ct) =>
        {
            ReportService.RejectBotDevice(request.DeviceId);
            var created = await svc.CreateAsync(request, ct);
            events.ReportFiled(created, request.DeviceId); // AI triage + Telegram (new report or a merged repeat); never blocks
            return Results.Created($"/api/safety/reports/{created.Id}", created);
        })
            .WithName("CreateReport")
            .WithSummary("File a report (anonymous; deviceId is a random id made by the app). A repeat of an open report in the same cell is merged as a confirmation.")
            .Produces<ReportDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        g.MapPost("/reports/{id}/confirm", async (string id, ConfirmReportRequest request, ReportService svc, CancellationToken ct) =>
        {
            ReportService.RejectBotDevice(request.DeviceId);
            return await svc.ConfirmAsync(id, request.DeviceId, ct) is { } r ? Results.Ok(r) : Results.NotFound();
        })
            .WithName("ConfirmReport")
            .WithSummary("\"Still true\": adds the device as an independent supporter and keeps the report alive.")
            .Produces<ReportDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        g.MapGet("/alerts", async (double lat, double lon, string? deviceId, AlertService svc, CancellationToken ct) =>
        {
            var point = new GeoPoint(lat, lon);
            if (!GridSpec.IsInArea(point)) return Results.ValidationProblem(Problem("lat,lon", "The location must be in or around Kraków."));
            ReportService.RejectBotDevice(deviceId);
            return Results.Ok(await svc.ForPointAsync(point, deviceId, ct));
        })
            .WithName("GetAlertsForPoint")
            .WithSummary("Active planner alerts whose area covers this point. Apps call this about once a minute while online.")
            .Produces<IReadOnlyList<PlannerAlertDto>>()
            .ProducesValidationProblem();

        // ── Planner (X-Planner-Key) ──────────────────────────────────────────
        var p = g.MapGroup("/planner").AddEndpointFilter<PlannerKeyFilter>();

        p.MapGet("/ping", () => Results.Ok(new { ok = true })).WithName("PlannerPing").WithSummary("Checks the planner key.");

        p.MapGet("/weights", async (WeightService svc, CancellationToken ct) => Results.Ok(await svc.GetAsync(ct)))
            .WithName("GetFactorWeights")
            .WithSummary("The factor weights in use for every layer, with the default, what each factor measures and why it is weighted that way.")
            .Produces<WeightsDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        p.MapPut("/weights", async (SetWeightsRequest request, WeightService svc, CancellationToken ct) => Results.Ok(await svc.SetAsync(request, ct)))
            .WithName("SetFactorWeights")
            .WithSummary("Sets factor weights (factor key to any non-negative number). Each layer is scaled to add up to 100. Changes every score for everyone.")
            .Produces<WeightsDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        p.MapDelete("/weights", async (WeightService svc, CancellationToken ct) => Results.Ok(await svc.ResetAsync(ct)))
            .WithName("ResetFactorWeights")
            .WithSummary("Goes back to the default weights.")
            .Produces<WeightsDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        p.MapGet("/route-thresholds", async (RouteThresholdService svc, CancellationToken ct) => Results.Ok(await svc.GetAsync(ct)))
            .WithName("GetRouteThresholds")
            .WithSummary("Per safety measure: how good the fastest walking route must be (average and weakest stretch) before no safer alternative is searched for, with the default and why.")
            .Produces<RouteThresholdsDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        p.MapPut("/route-thresholds", async (SetRouteThresholdsRequest request, RouteThresholdService svc, CancellationToken ct) => Results.Ok(await svc.SetAsync(request, ct)))
            .WithName("SetRouteThresholds")
            .WithSummary("Sets the route thresholds per layer (Safety, Heat, Flood, Air): average and weakest stretch, 20 to 95, weakest not above average. Applies to every route for everyone.")
            .Produces<RouteThresholdsDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        p.MapDelete("/route-thresholds", async (RouteThresholdService svc, CancellationToken ct) => Results.Ok(await svc.ResetAsync(ct)))
            .WithName("ResetRouteThresholds")
            .WithSummary("Goes back to the default route thresholds.")
            .Produces<RouteThresholdsDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        p.MapGet("/summary", async (string? @event, int? top, PlannerService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetSummaryAsync(ScoreService.ParseEvent(@event), top ?? PlannerService.DefaultTop, ct)))
            .WithName("GetPlannerSummary")
            .WithSummary("Dashboard numbers for an event (heat | night | both): KPIs, score histogram, factor gaps, top priority places, report counts.")
            .Produces<PlannerSummaryDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        p.MapPost("/reports/{id}/verify", async (string id, ReportService svc, ISafetyEventSink events, CancellationToken ct) =>
            await svc.VerifyAsync(id, ct) is { } r ? Published(events, SafetyEventKind.ReportVerified, r) : Results.NotFound())
            .WithName("VerifyReport").WithSummary("Marks a report as checked so it counts in full.")
            .Produces<ReportDto>().ProducesProblem(StatusCodes.Status404NotFound);

        p.MapPost("/reports/{id}/resolve", async (string id, ResolveReportRequest request, ReportService svc, ISafetyEventSink events, CancellationToken ct) =>
            await svc.ResolveAsync(id, request.Note, ct) is { } r ? Published(events, SafetyEventKind.ReportResolved, r) : Results.NotFound())
            .WithName("ResolveReport").WithSummary("Marks a report fixed or dismissed; it stops affecting the score.")
            .Produces<ReportDto>().ProducesProblem(StatusCodes.Status404NotFound);

        p.MapGet("/reach", (double lat, double lon, double? radius, PresenceTracker presence) =>
        {
            var point = new GeoPoint(lat, lon);
            if (!GridSpec.IsInArea(point)) return Results.ValidationProblem(Problem("lat,lon", "The location must be in or around Kraków."));
            var r = Math.Clamp(radius ?? 1000, AlertService.MinRadiusMeters, AlertService.MaxRadiusMeters);
            return Results.Ok(new ReachDto(lat, lon, r, presence.CountNear(point, r), presence.CountActive(), (int)PresenceTracker.Window.TotalMinutes));
        })
            .WithName("GetAlertReach")
            .WithSummary("How many phones asked for alerts inside this circle in the last 15 minutes (anonymous count): the audience of an alert sent now.")
            .Produces<ReachDto>()
            .ProducesValidationProblem();

        p.MapGet("/alerts", async (bool? includeInactive, AlertService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(includeInactive == true, ct)))
            .WithName("GetPlannerAlerts").WithSummary("Alerts with the number of phones currently in each area.")
            .Produces<IReadOnlyList<PlannerAlertDto>>();

        p.MapPost("/alerts", async (CreateAlertRequest request, AlertService svc, ISafetyEventSink events, CancellationToken ct) =>
        {
            var created = await svc.CreateAsync(request, ct);
            events.Publish(new SafetyEvent(SafetyEventKind.AlertCreated, created.Id)); // Telegram to linked residents in the area
            return Results.Created($"/api/safety/planner/alerts/{created.Id}", created);
        })
            .WithName("CreateAlert").WithSummary("Creates an alert for everyone currently inside a circle.")
            .Produces<PlannerAlertDto>(StatusCodes.Status201Created).ProducesValidationProblem();

        p.MapDelete("/alerts/{id}", async (string id, AlertService svc, CancellationToken ct) =>
            await svc.CancelAsync(id, ct) is { } a ? Results.Ok(a) : Results.NotFound())
            .WithName("CancelAlert").WithSummary("Cancels an alert; apps stop showing it on their next check.")
            .Produces<PlannerAlertDto>().ProducesProblem(StatusCodes.Status404NotFound);

        p.MapGet("/agencies", (AgencyService svc) => Results.Ok(svc.List()))
            .WithName("GetAgencies").WithSummary("Agencies a planner can contact.")
            .Produces<IReadOnlyList<AgencyDto>>();

        p.MapPost("/dispatches", async (DispatchRequest request, AgencyService svc, CancellationToken ct) =>
        {
            var created = await svc.DispatchAsync(request, ct);
            return Results.Created($"/api/safety/planner/dispatches/{created.Id}", created);
        })
            .WithName("CreateDispatch")
            .WithSummary("Records a contact with an agency (simulated: nothing is sent) and returns a reference.")
            .Produces<DispatchDto>(StatusCodes.Status201Created).ProducesValidationProblem();

        p.MapGet("/dispatches", async (AgencyService svc, CancellationToken ct) => Results.Ok(await svc.ListDispatchesAsync(ct)))
            .WithName("GetDispatches").WithSummary("Log of agency contacts.")
            .Produces<IReadOnlyList<DispatchDto>>();

        p.MapPost("/demo-data", async (DemoDataService svc, CancellationToken ct) => Results.Ok(new { created = await svc.SeedAsync(ct) }))
            .WithName("SeedDemoData")
            .WithSummary("Adds sample citizen reports in the lowest-scoring areas (once). For demos.")
            .Produces(StatusCodes.Status200OK);

        return api;
    }

    private static Dictionary<string, string[]> Problem(string field, string message) => new() { [field] = [message] };

    /// <summary>Tells the resident(s) behind a report that a planner verified or resolved it (Telegram, if they linked it).</summary>
    private static IResult Published(ISafetyEventSink events, SafetyEventKind kind, ReportDto report)
    {
        events.Publish(new SafetyEvent(kind, report.Id));
        return Results.Ok(report);
    }

    internal static bool IsPlanner(HttpContext http, SafetyOptions options) =>
        http.Request.Headers.TryGetValue(PlannerKeyHeader, out var value) && KeyMatches(value.ToString(), options.PlannerKey);

    internal static bool KeyMatches(string supplied, string expected)
    {
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}

/// <summary>Rejects planner requests without a valid <c>X-Planner-Key</c> header.</summary>
public sealed class PlannerKeyFilter(IOptions<SafetyOptions> options) : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        SafetyEndpoints.IsPlanner(context.HttpContext, options.Value)
            ? next(context)
            : ValueTask.FromResult<object?>(Results.Problem(
                title: "Planner key required",
                detail: $"Send the planner key in the {SafetyEndpoints.PlannerKeyHeader} header.",
                statusCode: StatusCodes.Status401Unauthorized));
}

/// <summary>Turns safety validation and rate-limit errors into 400 / 429 problem responses.</summary>
public sealed class SafetyErrorsFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (SafetyValidationException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [ex.Field] = [ex.Message] });
        }
        catch (SafetyRateLimitException ex)
        {
            return Results.Problem(title: "Too many reports", detail: ex.Message, statusCode: StatusCodes.Status429TooManyRequests);
        }
    }
}
