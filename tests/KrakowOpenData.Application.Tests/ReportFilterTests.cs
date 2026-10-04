using KrakowOpenData.Application.Safety;
using KrakowOpenData.Contracts;
using KrakowOpenData.Domain.Safety;

namespace KrakowOpenData.Application.Tests;

public class ReportFilterTests
{
    private static async Task<(SafetyWorld World, ReportService Reports)> Seeded()
    {
        var world = new SafetyWorld();
        var reports = world.Reports();
        var here = SafetyWorld.Centre;
        var away = SafetyWorld.Offset(here, 0, 800);   // a different 250 m square

        async Task Add(string type, GeoPointLike at, string device, string? note = null) =>
            await reports.CreateAsync(new CreateReportRequest(type, at.Lat, at.Lon, note, device));

        await Add("LightOut", new(here), "device-0000001", "Lamp out at the bakery");
        await Add("UnsafeAtNight", new(here), "device-0000002", "Dark underpass");
        await Add("NoShade", new(away), "device-0000003", "Bus stop in full sun");
        await Add("FloodedStreet", new(away), "device-0000004", "Water over the road");
        await Add("SmokeOrBurning", new(here), "device-0000005");
        return (world, reports);
    }

    private readonly record struct GeoPointLike(double Lat, double Lon)
    {
        public GeoPointLike(KrakowOpenData.Domain.Common.GeoPoint p) : this(p.Latitude, p.Longitude) { }
    }

    [Fact]
    public async Task By_default_only_open_reports_are_listed()
    {
        var (world, reports) = await Seeded();
        var first = (await reports.ListAsync(new ReportFilter(), true, 100)).First();
        await reports.ResolveAsync(first.Id, "fixed");

        Assert.Equal(4, (await reports.ListAsync(new ReportFilter(), true, 100)).Count);
        Assert.Equal(5, (await reports.ListAsync(new ReportFilter(Status: ReportStatusFilter.All), true, 100)).Count);
        var resolved = await reports.ListAsync(new ReportFilter(Status: ReportStatusFilter.Resolved), true, 100);
        Assert.Equal(first.Id, Assert.Single(resolved).Id);
    }

    [Theory]
    [InlineData(ScoreLayer.Safety, 2)]
    [InlineData(ScoreLayer.Heat, 1)]
    [InlineData(ScoreLayer.Flood, 1)]
    [InlineData(ScoreLayer.Air, 1)]
    public async Task Reports_can_be_filtered_by_layer(ScoreLayer layer, int expected)
    {
        var (_, reports) = await Seeded();
        var list = await reports.ListAsync(new ReportFilter(Layer: layer), true, 100);
        Assert.Equal(expected, list.Count);
        Assert.All(list, r => Assert.Equal(layer.ToString(), r.Layer));
    }

    [Fact]
    public async Task Reports_can_be_filtered_by_type()
    {
        var (_, reports) = await Seeded();
        var list = await reports.ListAsync(new ReportFilter(Type: ReportType.FloodedStreet), true, 100);
        Assert.Equal("FloodedStreet", Assert.Single(list).Type);
    }

    [Fact]
    public async Task Reports_can_be_filtered_by_verification()
    {
        var (_, reports) = await Seeded();
        var target = (await reports.ListAsync(new ReportFilter(Type: ReportType.NoShade), true, 100)).Single();
        await reports.VerifyAsync(target.Id);

        Assert.Equal(target.Id, Assert.Single(await reports.ListAsync(new ReportFilter(Verified: true), true, 100)).Id);
        Assert.Equal(4, (await reports.ListAsync(new ReportFilter(Verified: false), true, 100)).Count);
    }

    [Fact]
    public async Task Free_text_searches_notes_type_names_and_areas_for_planners_only()
    {
        var (_, reports) = await Seeded();

        Assert.Single(await reports.ListAsync(new ReportFilter(Text: "dark under"), includeNotes: true, 100));
        Assert.Single(await reports.ListAsync(new ReportFilter(Text: "FLOODED"), includeNotes: true, 100));        // type label, any case
        Assert.Single(await reports.ListAsync(new ReportFilter(Text: "bakery"), includeNotes: true, 100));

        // Residents never see notes, so a note must not be findable by them.
        Assert.Empty(await reports.ListAsync(new ReportFilter(Text: "dark under"), includeNotes: false, 100));
        Assert.Single(await reports.ListAsync(new ReportFilter(Text: "flooded"), includeNotes: false, 100));   // but the type label is public
    }

    [Fact]
    public async Task Filters_are_applied_before_the_limit()
    {
        var (_, reports) = await Seeded();
        // Newest activity first: the flood report is not among the 2 newest overall, yet a layer filter must still find it.
        var all = await reports.ListAsync(new ReportFilter(), true, 2);
        Assert.DoesNotContain(all, r => r.Type == "FloodedStreet");
        var flood = await reports.ListAsync(new ReportFilter(Layer: ScoreLayer.Flood), true, 2);
        Assert.Equal("FloodedStreet", Assert.Single(flood).Type);
    }

    [Fact]
    public async Task Filters_combine()
    {
        var (_, reports) = await Seeded();
        var list = await reports.ListAsync(new ReportFilter(Layer: ScoreLayer.Safety, Text: "dark under"), true, 100);
        Assert.Equal("UnsafeAtNight", Assert.Single(list).Type);
        Assert.Empty(await reports.ListAsync(new ReportFilter(Layer: ScoreLayer.Heat, Text: "dark under"), true, 100));
    }

    [Fact]
    public async Task Near_a_point_only_reports_inside_the_radius_are_listed()
    {
        var (_, reports) = await Seeded();
        var near = await reports.ListAsync(new ReportFilter(SafetyWorld.Centre, 300), true, 100);
        Assert.Equal(3, near.Count);   // the three at the centre, not the two 800 m away
    }
}
