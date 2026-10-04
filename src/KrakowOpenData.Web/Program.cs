using KrakowOpenData.Web.Components;
using KrakowOpenData.Web.Localization;
using KrakowOpenData.Client;
using KrakowOpenData.Web.Assets;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddScoped<LanguageState>(); // one per browser session (circuit)
builder.Services.AddSingleton<AssetVersions>(); // content-hashed css/js URLs, so Cloudflare never serves a previous build

// The UI reads everything through KrakowOpenData.Client, like any external consumer would.
builder.Services.AddKrakowOpenDataClient(builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5080/");

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

// The Kompas Krakowa app files are revalidated on every load (ETag), so a new version is never hidden by the browser cache.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        if (ctx.Context.Request.Path.StartsWithSegments("/safety")) ctx.Context.Response.Headers.CacheControl = "no-cache";
    }
});

// Kompas Krakowa progressive web app (static files in wwwroot/safety). It calls the API from the browser, so it needs the
// API address as the browser sees it, which can differ from the one this server uses (e.g. inside Docker).
var publicApi = builder.Configuration["Safety:PublicApiBaseUrl"] ?? builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5080/";
// Demo convenience: when Safety:PlannerDemoKey is set, the planner dashboard signs in with it so the menu link opens it directly.
// It is the same shared demo key the API checks (Safety:PlannerKey). Leave it empty outside demos: the planner then asks for the key.
var plannerAutoKey = builder.Configuration["Safety:PlannerDemoKey"] ?? string.Empty;
// Where people report accessibility problems (an e-mail address or a link), shown in the accessibility statement (#/accessibility).
var accessibilityContact = builder.Configuration["Safety:AccessibilityContact"] ?? string.Empty;
app.MapGet("/safety/config.js", () => Results.Text(
    $"window.KRK_CONFIG = {{ apiBase: {System.Text.Json.JsonSerializer.Serialize(publicApi.TrimEnd('/'))}, plannerAutoKey: {System.Text.Json.JsonSerializer.Serialize(plannerAutoKey)}, accessibilityContact: {System.Text.Json.JsonSerializer.Serialize(accessibilityContact)} }};",
    "application/javascript"));
// One route covers /safety and /safety/ (two would clash and answer 500).
app.MapGet("/safety", () => Results.Redirect("/safety/index.html"));
app.MapGet("/safety/planner", () => Results.Redirect("/safety/index.html#/planner"));
app.UseAntiforgery();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
