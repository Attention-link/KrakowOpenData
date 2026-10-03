namespace KrakowOpenData.Client;

/// <summary>
/// Entry point to every area of the API. Inject this when you need several areas, or inject a single
/// area interface (e.g. <see cref="IEnvironmentClient"/>) when you need only one.
/// </summary>
public interface IKrakowOpenDataClient
{
    ICatalogClient Catalog { get; }
    IMobilityClient Mobility { get; }
    IEnvironmentClient Environment { get; }
    IClimateCrisisClient Crisis { get; }
    IUrbanSpaceClient Urban { get; }
    IPublicServicesClient Services { get; }
    IOpenDataClient OpenData { get; }
}
