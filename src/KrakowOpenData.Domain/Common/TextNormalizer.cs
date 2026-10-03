using System.Globalization;
using System.Text;

namespace KrakowOpenData.Domain.Common;

/// <summary>
/// Normalises Polish (and other) text for search: lower-case, no diacritics, single spaces.
/// "Kraków Główny" and "krakow glowny" become equal.
/// </summary>
public static class TextNormalizer
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        // 'ł' and 'Ł' do not decompose under Unicode normalisation, so map them explicitly.
        var prepared = text.Replace('ł', 'l').Replace('Ł', 'L');
        var decomposed = prepared.Normalize(NormalizationForm.FormD);

        var builder = new StringBuilder(decomposed.Length);
        var lastWasSpace = false;
        foreach (var c in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark) continue;

            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace && builder.Length > 0) builder.Append(' ');
                lastWasSpace = true;
                continue;
            }

            builder.Append(char.ToLowerInvariant(c));
            lastWasSpace = false;
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>True when every word of <paramref name="query"/> appears in <paramref name="text"/>.</summary>
    public static bool ContainsAllWords(string? text, string? query)
    {
        var normalizedQuery = Normalize(query);
        if (normalizedQuery.Length == 0) return true;

        var normalizedText = Normalize(text);
        return normalizedQuery
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(word => normalizedText.Contains(word, StringComparison.Ordinal));
    }
}
