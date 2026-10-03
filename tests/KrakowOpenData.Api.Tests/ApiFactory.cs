using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace KrakowOpenData.Api.Tests;

/// <summary>Boots the real API in-process with built-in sample data, so tests need no network.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public ApiFactory()
    {
        // AddKrakowOpenData reads this flag while services are registered, so set it as an
        // environment variable too: those are guaranteed to be visible to WebApplication.CreateBuilder.
        Environment.SetEnvironmentVariable("KrakowData__UseSampleData", "true");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("KrakowData:UseSampleData", "true");
    }
}
