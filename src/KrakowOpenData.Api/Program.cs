using System.Text.Json.Serialization;
using KrakowOpenData.Api.Endpoints;
using KrakowOpenData.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKrakowOpenData(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
});

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (allowedOrigins.Length == 0) p.AllowAnyOrigin();
    else p.WithOrigins(allowedOrigins);
    p.AllowAnyHeader().WithMethods("GET");
}));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();

app.MapGet("/", () => Results.Redirect("/api/catalog")).ExcludeFromDescription();
app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTimeOffset.UtcNow }));

var api = app.MapGroup("/api").AddEndpointFilter<UpstreamUnavailableFilter>();
api.MapCatalogEndpoints();
api.MapMobilityEndpoints();
api.MapEnvironmentEndpoints();
api.MapClimateCrisisEndpoints();
api.MapUrbanSpaceEndpoints();
api.MapPublicServicesEndpoints();
api.MapOpenDataPortalEndpoints();

app.Run();

/// <summary>Exposed for WebApplicationFactory in integration tests.</summary>
public partial class Program
{
}
