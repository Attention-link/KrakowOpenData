using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Domain.Tests;

public class TextNormalizerTests
{
    [Theory]
    [InlineData("Kraków Główny", "krakow glowny")]
    [InlineData("Łagiewniki-Borek Fałęcki", "lagiewniki-borek falecki")]
    [InlineData("  Rondo   Mogilskie ", "rondo mogilskie")]
    [InlineData("ŻÓŁTA Gęś", "zolta ges")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalize_lowercases_and_strips_diacritics(string? input, string expected)
    {
        Assert.Equal(expected, TextNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("Plac Wszystkich Świętych", "swietych plac", true)]
    [InlineData("Plac Wszystkich Świętych", "wszystkich", true)]
    [InlineData("Plac Wszystkich Świętych", "rynek", false)]
    [InlineData("Anything", "", true)]
    public void ContainsAllWords_matches_every_word_in_any_order(string text, string query, bool expected)
    {
        Assert.Equal(expected, TextNormalizer.ContainsAllWords(text, query));
    }
}
