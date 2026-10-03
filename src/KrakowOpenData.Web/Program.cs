using KrakowOpenData.Web.Components;
using KrakowOpenData.Web.Localization;
using KrakowOpenData.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddScoped<LanguageState>(); // one per browser session (circuit)

var apiBaseUrl = builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5080/";
builder.Services.AddHttpClient<KrakowApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl.EndsWith('/') ? apiBaseUrl : apiBaseUrl + "/");
    client.Timeout = TimeSpan.FromSeconds(120); // first GTFS load can be slow
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
