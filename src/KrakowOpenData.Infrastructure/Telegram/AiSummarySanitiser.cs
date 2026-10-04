using System.Text.RegularExpressions;

namespace KrakowOpenData.Infrastructure.Telegram;

/// <summary>
/// Cleans an AI summary before it goes into a Telegram message. The summary is written by a model that read a resident's note, so
/// the note can steer it, and Telegram turns URLs, bare domains, @handles, e-mail addresses and phone numbers into tappable links
/// even in plain text. All of those are removed; what is left must still read as words, otherwise there is no summary.
/// </summary>
public static partial class AiSummarySanitiser
{
    private const int MinLetters = 3;

    // Order matters: whole URLs and e-mail addresses go before the bare domain and @handle patterns could split them.
    [GeneratedRegex(@"\b(?:https?|ftp)://\S*|\bwww\.\S*", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"[\w.+-]+@[\w-]+(?:\.[\w-]+)+")]
    private static partial Regex EmailRegex();

    /// <summary>"evil.tld", "t.me/x", "a.b.example.com/path": a dotted name ending in 2+ ASCII letters, with an optional path.</summary>
    [GeneratedRegex(@"\b[\w-]+(?:\.[\w-]+)*\.[A-Za-z]{2,24}\b(?:[/:?#]\S*)?")]
    private static partial Regex DomainRegex();

    [GeneratedRegex(@"@\w*")]
    private static partial Regex HandleRegex();

    /// <summary>6 or more digits, optionally with single spaces or dashes between them ("+48 600 700 800", "600-700-800").</summary>
    [GeneratedRegex(@"\+?\d(?:[ \t-]?\d){5,}")]
    private static partial Regex DigitsRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    /// <summary>The summary without anything Telegram would link, whitespace collapsed; null when nothing meaningful is left.</summary>
    public static string? Clean(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary)) return null;
        var text = UrlRegex().Replace(summary, " ");
        text = EmailRegex().Replace(text, " ");
        text = DomainRegex().Replace(text, " ");
        text = HandleRegex().Replace(text, " ");
        text = DigitsRegex().Replace(text, " ");
        text = WhitespaceRegex().Replace(text, " ").Trim();
        return text.Count(char.IsLetter) >= MinLetters ? text : null;
    }
}
