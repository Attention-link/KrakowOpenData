using KrakowOpenData.Application.Accessibility;
using KrakowOpenData.Domain.Accessibility;
using KrakowOpenData.Infrastructure.OpenStreetMap;
using KrakowOpenData.Infrastructure.Options;

namespace KrakowOpenData.Infrastructure.Tests;

public class AccessFeaturesParserTests
{
    private static readonly string Json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "osm-access-sample.json"));

    private static IReadOnlyList<AccessFeature> Items => AccessFeaturesParser.Parse(Json);

    private static AccessFeature Get(string id) => Items.Single(i => i.Id == id);

    [Fact]
    public void Maps_every_usable_element_and_skips_the_rest()
    {
        var ids = Items.Select(i => i.Id).ToHashSet();

        Assert.Equal(17, ids.Count);
        Assert.DoesNotContain("node/11", ids);   // parking: nothing about accessibility
        Assert.DoesNotContain("node/12", ids);   // only a wheelchair tag: no name, no kind
        Assert.DoesNotContain("node/13", ids);   // no tags
        Assert.DoesNotContain("way/107", ids);   // geometry with a single point
    }

    [Fact]
    public void Kinds_are_recognised()
    {
        Assert.Equal(AccessKind.Kerb, Get("node/1").Kind);
        Assert.Equal(AccessKind.Elevator, Get("node/4").Kind);
        Assert.Equal(AccessKind.Elevator, Get("way/106").Kind);
        Assert.Equal(AccessKind.Entrance, Get("node/5").Kind);
        Assert.Equal(AccessKind.Toilets, Get("node/7").Kind);
        Assert.Equal(AccessKind.Bench, Get("node/8").Kind);
        Assert.Equal(AccessKind.Place, Get("node/9").Kind);
        Assert.Equal(AccessKind.TactilePaving, Get("node/10").Kind);
        Assert.Equal(AccessKind.Path, Get("way/100").Kind);
        Assert.Equal(AccessKind.Steps, Get("way/102").Kind);
    }

    [Fact]
    public void Items_carry_source_last_edit_and_links()
    {
        var kerb = Get("node/1");
        Assert.Equal("OpenStreetMap", kerb.Source);
        Assert.Equal(new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero), kerb.LastEdited);
        Assert.Equal("https://www.openstreetmap.org/node/1", kerb.OsmUrl);
        Assert.Equal("https://www.openstreetmap.org/edit?node=1", kerb.EditUrl);

        var steps = Get("way/102");
        Assert.NotNull(steps.Line);
        Assert.Equal(2, steps.Line!.Count);
        Assert.Equal("https://www.openstreetmap.org/edit?way=102", steps.EditUrl);
    }

    [Fact]
    public void Attributes_are_normalised_and_missing_values_stay_null()
    {
        var entrance = Get("node/5").Attributes;
        Assert.Equal("no", entrance.Wheelchair);
        Assert.Equal(2, entrance.StepCount);
        Assert.Equal(0.85, entrance.WidthMeters);
        Assert.Equal("hinged", entrance.Door);

        Assert.Equal(1.1, Get("node/4").Attributes.WidthMeters);
        Assert.Equal("paved_rough", Get("way/100").Attributes.SurfaceClass);
        Assert.Equal("sett", Get("way/100").Attributes.SurfaceRaw);
        Assert.Equal(8, Get("way/101").Attributes.InclinePercent);
        Assert.Equal("unpaved", Get("way/104").Attributes.SurfaceClass);

        var unknownPath = Get("way/105").Attributes;
        Assert.Null(unknownPath.SurfaceClass);
        Assert.Null(unknownPath.InclinePercent);
        Assert.Equal(AccessStatus.Unknown, Get("way/105").Status);

        Assert.False(Get("node/6").Attributes.HasAccessData);
        Assert.Null(Get("node/3").Attributes.Kerb);    // kerb=yes says nothing about the height
    }

    [Fact]
    public void Base_status_is_the_wheelchair_view_and_unknown_is_never_accessible()
    {
        Assert.Equal(AccessStatus.No, Get("node/1").Status);         // raised kerb
        Assert.Equal(AccessStatus.Yes, Get("node/2").Status);        // lowered kerb
        Assert.Equal(AccessStatus.Unknown, Get("node/3").Status);    // kerb type not given
        Assert.Equal(AccessStatus.Unknown, Get("way/106").Status);   // lift without a wheelchair tag
        Assert.Equal(AccessStatus.No, Get("way/102").Status);        // steps, no ramp mapped
        Assert.Equal(AccessStatus.Yes, Get("way/103").Status);       // steps with a wheelchair ramp
        Assert.Equal(AccessStatus.Limited, Get("node/9").Status);
    }

    [Fact]
    public void The_query_uses_the_configured_box_and_asks_for_timestamps()
    {
        var q = AccessFeaturesParser.BuildQuery(new GeoBoxOptions { MinLatitude = 50.035, MinLongitude = 19.9, MaxLatitude = 50.08, MaxLongitude = 19.99 });
        Assert.Contains("(50.035,19.9,50.08,19.99)", q);
        Assert.Contains("out center tags meta;", q);
        Assert.Contains("out geom tags meta;", q);
        Assert.Contains("highway\"=\"elevator", q);
        Assert.Contains("toilets:wheelchair", q);
    }
}

public class AccessTagNormaliserTests
{
    [Theory]
    [InlineData("asphalt", null, "paved_smooth")]
    [InlineData("paving_stones", "bad", "paved_rough")]
    [InlineData("paving_stones", "good", "paved_smooth")]
    [InlineData("sett", null, "paved_rough")]
    [InlineData("cobblestone", "excellent", "paved_rough")]
    [InlineData("gravel", null, "unpaved")]
    [InlineData("asphalt;gravel", null, "unpaved")]
    [InlineData("something_odd", null, null)]
    [InlineData(null, "good", null)]
    public void Surface_classes(string? surface, string? smoothness, string? expected) =>
        Assert.Equal(expected, AccessTagNormaliser.SurfaceClass(surface, smoothness));

    [Theory]
    [InlineData("8%", 8.0)]
    [InlineData("-4 %", 4.0)]
    [InlineData("3,5", 3.5)]
    [InlineData("up", null)]
    [InlineData("yes", null)]
    [InlineData("500%", null)]
    public void Incline(string raw, double? expected) => Assert.Equal(expected, AccessTagNormaliser.InclinePercent(raw));

    [Fact]
    public void Incline_in_degrees_becomes_percent() => Assert.Equal(3.5, AccessTagNormaliser.InclinePercent("2°"));

    [Theory]
    [InlineData("0.9", 0.9)]
    [InlineData("90 cm", 0.9)]
    [InlineData("1,5 m", 1.5)]
    [InlineData("3'", null)]
    [InlineData("0", null)]
    public void Width(string raw, double? expected) => Assert.Equal(expected, AccessTagNormaliser.WidthMeters(raw));

    [Theory]
    [InlineData("raised", "raised")]
    [InlineData("lowered", "lowered")]
    [InlineData("rolled", "lowered")]
    [InlineData("flush", "flush")]
    [InlineData("no", "flush")]
    [InlineData("yes", null)]
    public void Kerb(string raw, string? expected) => Assert.Equal(expected, AccessTagNormaliser.Kerb(raw));

    [Fact]
    public void Ramp_tags()
    {
        Assert.Equal(("wheelchair", false), AccessTagNormaliser.Ramp(k => k == "ramp:wheelchair" ? "yes" : null));
        Assert.Equal(("stroller", false), AccessTagNormaliser.Ramp(k => k == "ramp:stroller" ? "yes" : k == "ramp:wheelchair" ? "no" : null));
        Assert.Equal(((string?)null, true), AccessTagNormaliser.Ramp(k => k == "ramp" ? "no" : null));
        Assert.Equal(((string?)null, false), AccessTagNormaliser.Ramp(_ => null));
    }

    [Theory]
    [InlineData("designated", "yes")]
    [InlineData("limited", "limited")]
    [InlineData("no", "no")]
    [InlineData("unknown", null)]
    public void Wheelchair(string raw, string? expected) => Assert.Equal(expected, AccessTagNormaliser.Wheelchair(raw));
}

public class AccessRulesTests
{
    [Fact]
    public void A_stroller_ramp_suits_a_pram_but_only_partly_a_wheelchair()
    {
        var a = new AccessAttributes(Ramp: "stroller");
        Assert.Equal(AccessStatus.Yes, AccessRules.Status(AccessKind.Steps, a, AccessProfile.Pram));
        Assert.Equal(AccessStatus.Limited, AccessRules.Status(AccessKind.Steps, a, AccessProfile.Wheelchair));
    }

    [Fact]
    public void Steps_without_a_ramp_block_wheels_but_are_a_difficulty_for_walking()
    {
        var a = new AccessAttributes(StepCount: 5, Handrail: true);
        Assert.Equal(AccessStatus.No, AccessRules.Status(AccessKind.Steps, a, AccessProfile.Wheelchair));
        Assert.Equal(AccessStatus.No, AccessRules.Status(AccessKind.Steps, a, AccessProfile.Pram));
        Assert.Equal(AccessStatus.Limited, AccessRules.Status(AccessKind.Steps, a, AccessProfile.Mobility));
    }

    [Fact]
    public void Slope_limits_differ_per_profile()
    {
        var a = new AccessAttributes(SurfaceClass: "paved_smooth", InclinePercent: 7);
        Assert.Equal(AccessStatus.No, AccessRules.Status(AccessKind.Path, a, AccessProfile.Wheelchair));
        Assert.Equal(AccessStatus.Yes, AccessRules.Status(AccessKind.Path, a, AccessProfile.Pram));
    }

    [Fact]
    public void Missing_data_is_unknown_never_yes()
    {
        Assert.Equal(AccessStatus.Unknown, AccessRules.Status(AccessKind.Path, new AccessAttributes(InclinePercent: 2), AccessProfile.Wheelchair));
        Assert.Equal(AccessStatus.Unknown, AccessRules.Status(AccessKind.Entrance, new AccessAttributes(), AccessProfile.Pram));
        Assert.Equal(AccessStatus.Unknown, AccessRules.Status(AccessKind.Toilets, new AccessAttributes(), AccessProfile.Wheelchair));
    }

    [Fact]
    public void Reliability_depends_on_data_and_the_last_edit()
    {
        var now = new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal("osm_recent", AccessReliability.Of(true, now.AddMonths(-23), now));
        Assert.Equal("osm_old", AccessReliability.Of(true, now.AddMonths(-25), now));
        Assert.Equal("unknown", AccessReliability.Of(false, now, now));
    }

    [Fact]
    public void Profiles_parse_by_key()
    {
        Assert.Same(AccessProfile.Wheelchair, AccessProfile.Parse(null));
        Assert.Same(AccessProfile.Pram, AccessProfile.Parse("PRAM"));
        Assert.Null(AccessProfile.Parse("diagnosis"));
    }
}

public class WheelchairOnPlacesTests
{
    [Fact]
    public void Amenities_keep_the_wheelchair_tags_in_their_details_and_the_model_reads_them()
    {
        const string json = """
        {"elements":[{"type":"node","id":1,"lat":50.06,"lon":19.93,"tags":{"amenity":"toilets","toilets:wheelchair":"designated","changing_table":"yes"}},
                     {"type":"node","id":2,"lat":50.06,"lon":19.93,"tags":{"amenity":"toilets"}}]}
        """;
        var amenities = OverpassParser.Parse(json).Amenities;
        Assert.Contains("toilets:wheelchair: designated", amenities[0].Details);
        Assert.Equal("yes", KrakowOpenData.Application.Safety.SafetyModelProvider.WheelchairFrom(amenities[0].Details));
        Assert.Null(KrakowOpenData.Application.Safety.SafetyModelProvider.WheelchairFrom(amenities[1].Details));
        Assert.Equal("no", KrakowOpenData.Application.Safety.SafetyModelProvider.WheelchairFrom("access: yes; wheelchair: no"));
    }

    [Fact]
    public void Safety_places_carry_a_normalised_wheelchair_tag()
    {
        const string json = """
        {"elements":[{"type":"node","id":1,"lat":50.06,"lon":19.93,"tags":{"amenity":"pharmacy","wheelchair":"limited"}},
                     {"type":"node","id":2,"lat":50.06,"lon":19.93,"tags":{"amenity":"library"}}]}
        """;
        var places = SafetyPlacesParser.Parse(json);
        Assert.Equal("limited", places[0].Wheelchair);
        Assert.Null(places[1].Wheelchair);
    }
}
