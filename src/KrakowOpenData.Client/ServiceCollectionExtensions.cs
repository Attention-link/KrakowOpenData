using KrakowOpenData.Client.Areas;
using KrakowOpenData.Client.Http;
using Microsoft.Extensions.DependencyInjection;

namespace KrakowOpenData.Client;

/// <summary>Registers the client with dependency injection (ASP.NET Core, Blazor, worker services).</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Name of the HttpClient used by the client; configure it further via the returned builder.</summary>
    public const string HttpClientName = "KrakowOpenData";

    /// <summary>
    /// Registers <see cref="IKrakowOpenDataClient"/> and every area interface (<see cref="IMobilityClient"/>,
    /// <see cref="IEnvironmentClient"/>, …) against the API at <paramref name="baseAddress"/>.
    /// </summary>
    public static IHttpClientBuilder AddKrakowOpenDataClient(this IServiceCollection services, string baseAddress) =>
        services.AddKrakowOpenDataClient(o => o.BaseAddress = new Uri(baseAddress));

    public static IHttpClientBuilder AddKrakowOpenDataClient(this IServiceCollection services, Action<KrakowOpenDataClientOptions> configure)
    {
        var options = new KrakowOpenDataClientOptions();
        configure(options);

        services.AddTransient<IApiTransport>(sp =>
            new ApiTransport(sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName)));
        services.AddTransient<ICatalogClient, CatalogClient>();
        services.AddTransient<IMobilityClient, MobilityClient>();
        services.AddTransient<IEnvironmentClient, EnvironmentClient>();
        services.AddTransient<IClimateCrisisClient, ClimateCrisisClient>();
        services.AddTransient<IUrbanSpaceClient, UrbanSpaceClient>();
        services.AddTransient<IPublicServicesClient, PublicServicesClient>();
        services.AddTransient<IOpenDataClient, OpenDataClient>();
        services.AddTransient<IKrakowOpenDataClient>(sp => new KrakowOpenDataClient(
            sp.GetRequiredService<ICatalogClient>(),
            sp.GetRequiredService<IMobilityClient>(),
            sp.GetRequiredService<IEnvironmentClient>(),
            sp.GetRequiredService<IClimateCrisisClient>(),
            sp.GetRequiredService<IUrbanSpaceClient>(),
            sp.GetRequiredService<IPublicServicesClient>(),
            sp.GetRequiredService<IOpenDataClient>()));

        return services
            .AddHttpClient(HttpClientName, http => Configure(http, options))
            .AddHttpMessageHandler(() => new RetryUpstreamHandler(options.MaxRetries));
    }

    internal static void Configure(HttpClient http, KrakowOpenDataClientOptions options)
    {
        var url = options.BaseAddress.ToString();
        http.BaseAddress = new Uri(url.EndsWith('/') ? url : url + "/");
        http.Timeout = options.Timeout;
    }
}
