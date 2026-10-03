namespace KrakowOpenData.Domain.Common;

/// <summary>
/// What a dataset is about. Every dataset in the catalog belongs to exactly one category,
/// and the API and UI are organised by these categories.
/// </summary>
public enum DataCategory
{
    /// <summary>Public transport, park and ride, bikes.</summary>
    Mobility,

    /// <summary>Weather observations and air quality.</summary>
    Environment,

    /// <summary>River levels, weather warnings, flood zones.</summary>
    ClimateAndCrisis,

    /// <summary>Districts, land use, green areas, demographics.</summary>
    UrbanSpace,

    /// <summary>City procedures, offices and service cards.</summary>
    PublicServices,

    /// <summary>Residents, labour market, tourism, culture, education and sport statistics.</summary>
    Society
}
