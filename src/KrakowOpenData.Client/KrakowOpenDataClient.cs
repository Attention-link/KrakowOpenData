using KrakowOpenData.Client.Areas;
using KrakowOpenData.Client.Http;

namespace KrakowOpenData.Client;

/// <summary>
/// Default <see cref="IKrakowOpenDataClient"/>. With dependency injection use
/// <see cref="ServiceCollectionExtensions.AddKrakowOpenDataClient(Microsoft.Extensions.DependencyInjection.IServiceCollection, string)"/>;
/// without it use <see cref="Create(string)"/> or <see cref="Create(KrakowOpenDataClientOptions)"/>.
/// </summary>
public sealed class KrakowOpenDataClient : IKrakowOpenDataClient, IDisposable
{
    private readonly HttpClient? _ownedHttpClient;

    public KrakowOpenDataClient(
        ICatalogClient catalog,
        IMobilityClient mobility,
        IEnvironmentClient environment,
        IClimateCrisisClient crisis,
        IUrbanSpaceClient urban,
        IPublicServicesClient services,
        IOpenDataClient openData)
    {
        Catalog = catalog;
        Mobility = mobility;
        Environment = environment;
        Crisis = crisis;
        Urban = urban;
        Services = services;
        OpenData = openData;
    }

    /// <summary>Uses an HttpClient you configured (its BaseAddress must point at the API). You keep ownership of it.</summary>
    public KrakowOpenDataClient(HttpClient httpClient) : this(new ApiTransport(httpClient))
    {
    }

    private KrakowOpenDataClient(IApiTransport api, HttpClient? owned = null)
        : this(new CatalogClient(api), new MobilityClient(api), new EnvironmentClient(api), new ClimateCrisisClient(api),
            new UrbanSpaceClient(api), new PublicServicesClient(api), new OpenDataClient(api))
    {
        _ownedHttpClient = owned;
    }

    public ICatalogClient Catalog { get; }
    public IMobilityClient Mobility { get; }
    public IEnvironmentClient Environment { get; }
    public IClimateCrisisClient Crisis { get; }
    public IUrbanSpaceClient Urban { get; }
    public IPublicServicesClient Services { get; }
    public IOpenDataClient OpenData { get; }

    /// <summary>Creates a standalone client (with retries) for scripts and console apps. Dispose it when done.</summary>
    public static KrakowOpenDataClient Create(string baseAddress) =>
        Create(new KrakowOpenDataClientOptions { BaseAddress = new Uri(baseAddress) });

    public static KrakowOpenDataClient Create(KrakowOpenDataClientOptions options)
    {
        var http = new HttpClient(new RetryUpstreamHandler(options.MaxRetries) { InnerHandler = new HttpClientHandler() });
        ServiceCollectionExtensions.Configure(http, options);
        return new KrakowOpenDataClient(new ApiTransport(http), http);
    }

    public void Dispose() => _ownedHttpClient?.Dispose();
}
