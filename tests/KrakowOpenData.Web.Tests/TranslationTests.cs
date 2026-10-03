using System.Text.RegularExpressions;
using KrakowOpenData.Application.Catalog;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Web.Localization;

namespace KrakowOpenData.Web.Tests;

public class TranslationTests
{
    public static TheoryData<string> AllKeys()
    {
        var data = new TheoryData<string>();
        foreach (var key in UiText.Entries.Keys) data.Add(key);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllKeys))]
    public void Every_entry_has_polish_english_and_ukrainian_text(string key)
    {
        foreach (var language in AppLanguages.All)
        {
            var text = UiText.Entries[key][(int)language];
            Assert.False(string.IsNullOrWhiteSpace(text), $"'{key}' has no {language} text.");
        }
    }

    [Theory]
    [MemberData(nameof(AllKeys))]
    public void Placeholders_match_in_every_language(string key)
    {
        static string Placeholders(string text) =>
            string.Join(",", Regex.Matches(text, @"\{(\d+)\}").Select(m => m.Groups[1].Value).Order());

        var english = Placeholders(UiText.Entries[key][(int)AppLanguage.En]);
        foreach (var language in AppLanguages.All)
        {
            Assert.Equal(english, Placeholders(UiText.Entries[key][(int)language]));
        }
    }

    [Fact]
    public void Every_catalog_dataset_has_translated_title_and_notes()
    {
        foreach (var dataset in DataCatalog.Datasets)
        {
            Assert.True(UiText.Entries.ContainsKey("ds." + dataset.Key), $"Missing ds.{dataset.Key}");
            Assert.True(UiText.Entries.ContainsKey("ds." + dataset.Key + ".notes"), $"Missing ds.{dataset.Key}.notes");
        }
    }

    [Fact]
    public void Every_open_data_table_has_a_translated_title()
    {
        foreach (var table in OpenDataTables.All)
            Assert.True(UiText.Entries.ContainsKey("odt." + table.Key), $"Missing odt.{table.Key}");
    }

    [Fact]
    public void Every_category_has_a_translated_name_and_description()
    {
        foreach (var category in Enum.GetValues<DataCategory>())
        {
            Assert.True(UiText.Entries.ContainsKey("cat." + category), $"Missing cat.{category}");
            Assert.True(UiText.Entries.ContainsKey("cat." + category + ".desc"), $"Missing cat.{category}.desc");
        }
    }

    [Theory]
    [InlineData("access.", new[] { "LiveFeed", "Api", "Download", "Seed", "Sample", "Planned" })]
    [InlineData("mode.", new[] { "Tram", "Bus", "Rail", "Metro", "Other" })]
    [InlineData("band.", new[] { "Good", "Moderate", "Poor", "VeryPoor", "Unknown" })]
    [InlineData("state.", new[] { "AboveAlarm", "AboveWarning", "Normal", "Unknown" })]
    public void Values_shown_from_the_api_are_translated(string prefix, string[] values)
    {
        foreach (var value in values) Assert.True(UiText.Entries.ContainsKey(prefix + value), $"Missing {prefix}{value}");
    }

    [Fact]
    public void Same_key_gives_different_text_per_language()
    {
        Assert.Equal("Weather", Translator.For(AppLanguage.En)["nav.weather"]);
        Assert.Equal("Pogoda", Translator.For(AppLanguage.Pl)["nav.weather"]);
        Assert.Equal("Погода", Translator.For(AppLanguage.Uk)["nav.weather"]);
    }

    [Fact]
    public void Unknown_key_shows_the_key_and_Or_uses_the_fallback()
    {
        var t = Translator.For(AppLanguage.Pl);
        Assert.Equal("no.such.key", t["no.such.key"]);
        Assert.Equal("Fallback", t.Or("no.such.key", "Fallback"));
        Assert.Equal("Objazd", t.Or("effect.DETOUR", "DETOUR"));
        Assert.False(t.Has("no.such.key"));
    }

    [Fact]
    public void Formatting_fills_placeholders()
    {
        Assert.Equal("Strona 2 z 5", Translator.For(AppLanguage.Pl).F("pager.page", 2, 5));
        Assert.Equal("Сторінка 2 з 5", Translator.For(AppLanguage.Uk).F("pager.page", 2, 5));
    }

    [Theory]
    [InlineData(AppLanguage.En, 0, "on time")]
    [InlineData(AppLanguage.Pl, 0, "punktualnie")]
    [InlineData(AppLanguage.En, 180, "+3 min")]
    [InlineData(AppLanguage.Uk, 180, "+3 хв")]
    [InlineData(AppLanguage.Pl, -60, "-1 min")]
    [InlineData(AppLanguage.En, null, "–")]
    public void Delays_are_worded_per_language(AppLanguage language, int? seconds, string expected)
    {
        Assert.Equal(expected, Translator.For(language).Delay(seconds));
    }

    [Theory]
    [InlineData(AppLanguage.En, true, "Yes")]
    [InlineData(AppLanguage.Pl, false, "Nie")]
    [InlineData(AppLanguage.Uk, null, "Невідомо")]
    public void Yes_no_unknown_are_translated(AppLanguage language, bool? value, string expected)
    {
        Assert.Equal(expected, Translator.For(language).YesNo(value));
    }
}

public class AppLanguageTests
{
    [Theory]
    [InlineData("pl", AppLanguage.Pl)]
    [InlineData("pl-PL", AppLanguage.Pl)]
    [InlineData("EN-gb", AppLanguage.En)]
    [InlineData("uk", AppLanguage.Uk)]
    [InlineData("uk-UA", AppLanguage.Uk)]
    [InlineData("ua", AppLanguage.Uk)]
    public void Browser_and_stored_codes_are_recognised(string code, AppLanguage expected)
    {
        Assert.Equal(expected, AppLanguages.TryParse(code));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("de-DE")]
    public void Unsupported_codes_give_null(string? code)
    {
        Assert.Null(AppLanguages.TryParse(code));
    }

    [Fact]
    public void Codes_round_trip()
    {
        foreach (var language in AppLanguages.All)
        {
            Assert.Equal(language, AppLanguages.TryParse(language.Code()));
        }
    }

    [Fact]
    public void Switcher_offers_exactly_polish_english_and_ukrainian()
    {
        Assert.Equal(new[] { "PL", "EN", "UA" }, AppLanguages.All.Select(l => l.ShortLabel()).ToArray());
    }
}

public class LanguageStateTests
{
    [Fact]
    public void Starts_in_the_default_language()
    {
        Assert.Equal(AppLanguages.Default, new LanguageState().Current.Language);
    }

    [Fact]
    public void Changing_language_notifies_once_and_repeating_it_does_not()
    {
        var state = new LanguageState();
        var notifications = 0;
        state.Changed += () => notifications++;

        state.Set(AppLanguage.Uk);
        state.Set(AppLanguage.Uk);

        Assert.Equal(AppLanguage.Uk, state.Current.Language);
        Assert.Equal(1, notifications);
    }
}
