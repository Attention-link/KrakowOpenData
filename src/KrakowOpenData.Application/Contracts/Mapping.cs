using KrakowOpenData.Application.Catalog;
using KrakowOpenData.Domain.ClimateAndCrisis;
using KrakowOpenData.Domain.Environment;
using KrakowOpenData.Domain.Mobility;
using KrakowOpenData.Domain.PublicServices;
using KrakowOpenData.Domain.UrbanSpace;

namespace KrakowOpenData.Application.Contracts;

/// <summary>Entity → DTO mappings, kept in one place.</summary>
public static class Mapping
{
    public static DatasetDto ToDto(this DatasetDescriptor d) =>
        new(d.Key, d.Title, d.Category.ToString(), d.Publisher, d.SourceUrl, d.Format, d.Access.ToString(), d.ApiRoute, d.Notes);

    public static StopDto ToDto(this TransitStop s, double? distanceMeters = null) =>
        new(s.Id, s.Code, s.Name, s.Location.Latitude, s.Location.Longitude, s.FeedKey, s.WheelchairAccessible,
            distanceMeters is null ? null : Math.Round(distanceMeters.Value, 1));

    public static RouteDto ToDto(this TransitRoute r) =>
        new(r.Id, r.ShortName, r.LongName, r.Mode.ToString(), r.Color, r.FeedKey);

    public static VehicleDto ToDto(this VehiclePosition v, TransitRoute? route) =>
        new(v.Id, v.VehicleLabel, v.TripId, v.RouteId, route?.ShortName, (route?.Mode ?? TransportMode.Other).ToString(),
            v.Location.Latitude, v.Location.Longitude, v.Bearing,
            v.SpeedMetersPerSecond is { } mps ? (float)Math.Round(mps * 3.6, 1) : null,
            v.Timestamp, v.WheelchairAccessible);

    public static TripUpdateDto ToDto(this TripUpdate t) =>
        new(t.Id, t.TripId, t.RouteId, t.DelaySeconds, t.StopTimeUpdates.Count, t.Timestamp);

    public static AlertDto ToDto(this ServiceAlert a) =>
        new(a.Id, a.Header, a.Description, a.Cause, a.Effect, a.RouteIds, a.StopIds,
            a.ActivePeriods.Select(p => p.Start).Where(s => s.HasValue).DefaultIfEmpty().Min(),
            a.ActivePeriods.Select(p => p.End).Where(e => e.HasValue).DefaultIfEmpty().Max(),
            a.Url);

    public static ParkAndRideDto ToDto(this ParkAndRideFacility p) =>
        new(p.Id, p.Name, p.Address, p.Location?.Latitude, p.Location?.Longitude, p.Capacity, p.EvChargers,
            p.BikeSpaces, p.OpeningHours, p.Notes, p.Source);

    public static WeatherDto ToDto(this WeatherObservation w) =>
        new(w.Id, w.StationName, w.ObservedAt, w.TemperatureC, w.WindSpeedMs, w.WindDirectionDegrees,
            w.RelativeHumidityPercent, w.PrecipitationMm, w.PressureHPa, w.IsHot, w.Source);

    public static AirQualityDto ToDto(this AirQualityMeasurement a) =>
        new(a.Id, a.StationName, a.Location?.Latitude, a.Location?.Longitude, a.MeasuredAt, a.Pm10, a.Pm25, a.No2,
            a.Band.ToString(), a.Source, a.OfficialIndex);

    public static RiverGaugeDto ToDto(this HydroObservation h) =>
        new(h.Id, h.StationName, h.River, h.Location?.Latitude, h.Location?.Longitude, h.WaterLevelCm, h.MeasuredAt,
            h.WarningLevelCm, h.AlarmLevelCm, h.State.ToString(), h.Source);

    public static WarningDto ToDto(this WeatherWarning w) =>
        new(w.Id, w.EventName, w.Level, w.ProbabilityPercent, w.ValidFrom, w.ValidTo, w.Content, w.Source);

    public static DistrictDto ToDto(this District d) =>
        new(d.Id, d.Number, d.Name, d.RegisteredPopulation, d.PopulationNote, d.Source);

    public static ServiceCardDto ToDto(this CityServiceCard c, int relevance = 0) =>
        new(c.Id, c.Title, c.Topic, c.Summary, c.Steps, c.Office, c.Address, c.OpeningHours, c.Phone, c.Fee,
            c.SourceUrl, relevance);

    public static WaitingListDto ToDto(this WaitingListEntry w) =>
        new(w.Id, w.Benefit, w.Provider, w.Place, w.Address, w.Locality, w.Phone, w.Location?.Latitude,
            w.Location?.Longitude, w.Urgent, w.PeopleWaiting, w.AverageWaitDays, w.FirstAvailableDate,
            w.StatisticsUpdated, w.StepFree, w.Toilet, w.CarPark, w.Source);

    public static OpenDataTableDto ToDto(this OpenDataTable t) =>
        new(t.Key, t.Title, t.Category.ToString(), t.ApiUrl);
}
