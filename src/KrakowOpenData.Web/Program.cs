using KrakowOpenData.Web.Components;
using KrakowOpenData.Web.Localization;
using KrakowOpenData.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddScoped<LanguageState>(); // one per browser session (circuit)

// The UI reads everything through KrakowOpenData.Client, like any external consumer would.
builder.Services.AddKrakowOpenDataClient(builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5080/");

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
