using KrakowOpenData.Domain.PublicServices;

namespace KrakowOpenData.Domain.Tests;

public class CityServiceCardTests
{
    private static readonly CityServiceCard Card = new(
        "GD-35",
        "Pobieranie otwartych danych przestrzennych",
        "Geodezja",
        "Export open data from MSIP",
        [],
        null, null, null, null, null,
        "https://bip.krakow.pl/uslugi/GD-35",
        ["msip", "mapa"]);

    [Fact]
    public void No_query_scores_zero()
    {
        Assert.Equal(0, Card.RelevanceFor(null));
        Assert.Equal(0, Card.RelevanceFor("   "));
    }

    [Fact]
    public void Exact_id_scores_highest()
    {
        Assert.True(Card.RelevanceFor("gd-35") > Card.RelevanceFor("msip"));
    }

    [Fact]
    public void Diacritics_are_ignored()
    {
        Assert.True(Card.RelevanceFor("przestrzennych") > 0);
        Assert.True(Card.RelevanceFor("geodezja") > 0);
    }

    [Fact]
    public void Unrelated_query_scores_zero()
    {
        Assert.Equal(0, Card.RelevanceFor("tramwaj"));
    }
}
