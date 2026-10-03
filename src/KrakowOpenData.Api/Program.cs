using System.Text.Json.Serialization;
using KrakowOpenData.Api.Endpoints;
using KrakowOpenData.Infrastructure;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKrakowOpenData(builder.Configuration);
builder.Services.AddProblemDetails();

// OpenAPI: spec at /swagger/v1/swagger.json, interactive docs at /swagger.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Kraków Open Data API",
        Version = "v1",
        Description = "Kraków's public data in one API: public transport, air quality, weather, rivers, warnings, " +
                      "districts, amenities, city procedures, NFZ waiting lists and the city's Open Data tables. " +
                      "503 means a public source is unavailable right now. .NET apps can use the KrakowOpenData.Client package."
    });
    o.SupportNonNullableReferenceTypes();
});
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

app.UseSwagger();
app.UseSwaggerUI(o =>
{
    o.SwaggerEndpoint("/swagger/v1/swagger.json", "Kraków Open Data API v1");
    o.DocumentTitle = "Kraków Open Data API";
});

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTimeOffset.UtcNow })).ExcludeFromDescription();

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
