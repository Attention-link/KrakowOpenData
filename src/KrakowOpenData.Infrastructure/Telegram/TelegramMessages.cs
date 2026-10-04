using System.Globalization;
using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Domain.Safety;

namespace KrakowOpenData.Infrastructure.Telegram;

/// <summary>
/// Every text the bot sends, in Polish (mirrors SafeWalk's messages.py). Plain text, no parse mode, so nothing a resident or a
/// model wrote can turn into markup. Location is always rounded to 3 decimals (about 100 m) and a resident's own note is never
/// put in a message to anyone else.
/// </summary>
public static class TelegramMessages
{
    public static class Kinds
    {
        public const string Linked = "linked", Unlinked = "unlinked", Received = "received", Merged = "merged", Verified = "verified",
            Resolved = "resolved", Alert = "alert", Staff = "staff";
    }

    public sealed record Message(string Text, object? Markup = null);

    private static readonly Dictionary<ReportType, string> Labels = new()
    {
        [ReportType.LightOut] = "Lampa nie świeci lub jest zbyt ciemno",
        [ReportType.UnsafeAtNight] = "Miejsce niebezpieczne nocą",
        [ReportType.PathHazard] = "Zablokowana lub niebezpieczna droga",
        [ReportType.WaterNotWorking] = "Punkt z wodą nie działa",
        [ReportType.NoShade] = "Brak cienia, bardzo gorące miejsce",
        [ReportType.HeatSpot] = "Przegrzany obszar, brak ulgi w pobliżu",
        [ReportType.FloodedStreet] = "Zalana ulica lub przejście podziemne",
        [ReportType.BlockedDrain] = "Zatkany odpływ lub wpust",
        [ReportType.RisingWater] = "Rzeka lub potok szybko wzbiera",
        [ReportType.SmokeOrBurning] = "Dym lub zapach spalenizny",
        [ReportType.StrongFumes] = "Silne opary lub zapach chemikaliów",
        [ReportType.DustCloud] = "Chmura pyłu lub pył budowlany"
    };

    public static string Label(ReportType type) => Labels.GetValueOrDefault(type, type.ToString());

    public static string Label(string type) => Ai.ReportTypeNames.Parse(type) is { } t ? Label(t) : type;

    // ── Linking ──────────────────────────────────────────────────────────────
    public static Message About(bool voice) => new(
        "To jest bot Kompasu Krakowa. Wysyła powiadomienia o alertach dla Twojej okolicy i o Twoich zgłoszeniach.\n" +
        "Aby je włączyć, w aplikacji wybierz „Powiadomienia w Telegramie”.\n" +
        (voice
            ? "Możesz też zgłosić problem tutaj: wyślij wiadomość głosową albo opisz go tekstem.\n"
            : "Możesz też zgłosić problem tutaj: opisz go tekstem.\n") +
        "/stop – wyłącz powiadomienia.");

    public static Message Linked() => new(
        "Połączono. Będziesz tu dostawać alerty dla Twojej okolicy oraz potwierdzenia i zmiany statusu Twoich zgłoszeń.\n" +
        "Aby wyłączyć, napisz /stop albo wyłącz powiadomienia w aplikacji.");

    public static Message LinkUsed() => new("Ten link został już użyty. Wygeneruj nowy w aplikacji.");

    public static Message LinkExpired() => new("Ten link wygasł (ważny 15 minut). Wygeneruj nowy w aplikacji.");

    public static Message LinkUnknown() => new("Ten link jest nieprawidłowy. Wygeneruj nowy w aplikacji.");

    public static Message Unlinked() => new("Powiadomienia wyłączone. Aby je włączyć ponownie, połącz się z aplikacji.");

    public static Message NothingToStop() => new("Ten czat nie jest połączony z aplikacją, więc nie ma czego wyłączać.");

    // ── The resident's own reports ───────────────────────────────────────────
    public static Message Received(CitizenReport r) => new(
        $"Otrzymaliśmy Twoje zgłoszenie: {Label(r.Type)}.\nStatus: niezweryfikowane. Dziękujemy! Damy znać, gdy ktoś z miasta je sprawdzi.");

    public static Message Merged(CitizenReport r) => new(
        $"Dziękujemy! To miejsce zostało już zgłoszone ({Label(r.Type)}). Twoje potwierdzenie wzmacnia zgłoszenie (potwierdzeń: {r.Supporters}).");

    public static Message Verified(CitizenReport r) => new(
        $"Twoje zgłoszenie „{Label(r.Type)}” zostało zweryfikowane przez pracownika miasta. Liczy się teraz w pełni w ocenie okolicy.");

    public static Message Resolved(CitizenReport r) => new(
        $"Twoje zgłoszenie „{Label(r.Type)}” zostało zamknięte (naprawione lub wyjaśnione)." +
        (string.IsNullOrWhiteSpace(r.ResolutionNote) ? string.Empty : $"\nNotatka miasta: {r.ResolutionNote}"));

    // ── Alerts ───────────────────────────────────────────────────────────────
    public static Message Alert(PlannerAlert alert, string? publicBaseUrl)
    {
        var severity = alert.Severity switch
        {
            AlertSeverity.Critical => "Alarm",
            AlertSeverity.Warning => "Ostrzeżenie",
            _ => "Informacja"
        };
        var message = alert.MessageTranslations.TryGetValue("pl", out var pl) && !string.IsNullOrWhiteSpace(pl) ? pl : alert.Message;
        var until = KrakowTime.ToLocal(alert.ExpiresAt);
        return new(
            $"🔴 {severity}: {alert.Title}\n{message}\n" +
            $"Obszar: {Math.Round(alert.RadiusMeters).ToString(CultureInfo.InvariantCulture)} m wokół punktu, mapa: {MapUrl(alert.Center)}\n" +
            $"Ważny do: {until.ToString("HH:mm, dd.MM.yyyy", CultureInfo.InvariantCulture)} (czas Krakowa)\n" +
            Footer(publicBaseUrl));
    }

    // ── Staff digest ─────────────────────────────────────────────────────────
    /// <summary>
    /// A new report for the staff chat. Never the device id, the report id or the resident's note: only the category, the AI
    /// suggestion (its summary only when the model flagged neither personal data nor abuse, cleaned by <see cref="AiSummarySanitiser"/>)
    /// and the rounded location.
    /// </summary>
    public static Message StaffDigest(CitizenReport r, string? publicBaseUrl)
    {
        var lines = new List<string>
        {
            $"🟠 Nowe zgłoszenie — {Label(r.Type)}",
            "Status: niezweryfikowane (zgłoszenie mieszkańca)"
        };
        if (r.Triage is { IsAbuse: false } ai)
        {
            var line = $"AI: ważność {Math.Clamp(ai.Severity, 1, 3)}/3";
            if (ai.SuggestedType is { } s && s != r.Type.ToString()) line += $" · kategoria? {Label(s)}";
            // A note can steer the model, so links, handles, e-mails and phone numbers are stripped (Telegram links them even in plain text).
            if (ai.SummaryIsShareable && AiSummarySanitiser.Clean(ai.SummaryPl) is { } summary) line += $" · „{summary}”";
            lines.Add(line + " (sugestia AI)");
        }
        else if (r.Triage is { IsAbuse: true })
        {
            lines.Add("AI: możliwe nadużycie, sprawdź przed działaniem (sugestia AI)");
        }

        lines.Add($"Okolica (±100 m): mapa: {MapUrl(r.Location)}");
        lines.Add(Footer(publicBaseUrl));
        return new(string.Join('\n', lines));
    }

    // ── Reporting through the bot (text or voice) ────────────────────────────
    public static object LocationKeyboard() => new Dictionary<string, object>
    {
        ["keyboard"] = new[] { new[] { new Dictionary<string, object> { ["text"] = "📍 Wyślij lokalizację", ["request_location"] = true } } },
        ["resize_keyboard"] = true,
        ["one_time_keyboard"] = true
    };

    public static object RemoveKeyboard() => new Dictionary<string, object> { ["remove_keyboard"] = true };

    public static object CategoryKeyboard() => new Dictionary<string, object>
    {
        ["inline_keyboard"] = Enum.GetValues<ReportType>()
            .Select(t => new[] { new Dictionary<string, object> { ["text"] = Label(t), ["callback_data"] = $"cat:{t}" } })
            .ToArray()
    };

    public static Message Transcript(string text, ReportType suggested) => new(
        $"Rozpoznany tekst: „{text}”\nSugestia AI: {Label(suggested)} (niezweryfikowane).\n" +
        "Wyślij teraz lokalizację przyciskiem poniżej (📎 → Lokalizacja), żeby utworzyć zgłoszenie.",
        LocationKeyboard());

    public static Message Heard(string text) => new($"Rozpoznany tekst: „{text}”");

    public static Message TextReceived(ReportType suggested) => new(
        $"Sugestia AI: {Label(suggested)}.\nWyślij teraz lokalizację przyciskiem poniżej (📎 → Lokalizacja), żeby utworzyć zgłoszenie.",
        LocationKeyboard());

    public static Message ChooseCategory() => new("Czego dotyczy zgłoszenie? Wybierz kategorię:", CategoryKeyboard());

    public static Message AskLocation(ReportType type) => new(
        $"Kategoria: {Label(type)}.\nWyślij teraz lokalizację przyciskiem poniżej, żeby utworzyć zgłoszenie.", LocationKeyboard());

    public static Message VoiceUnavailable() => new("Rozpoznawanie mowy jest teraz niedostępne. Opisz problem tekstem albo użyj aplikacji.");

    public static Message VoiceTooLong() => new("Nagranie jest za długie. Wyślij wiadomość głosową do 60 sekund.");

    public static Message VoiceLimited() => new("Wysłano już dużo nagrań w ostatniej godzinie. Opisz problem tekstem albo spróbuj później.");

    public static Message VoiceFailed() => new("Nie udało się rozpoznać nagrania. Spróbuj ponownie albo opisz problem tekstem.");

    public static Message NothingPending() => new("Najpierw opisz problem (tekstem albo głosem), potem wyślij lokalizację.", RemoveKeyboard());

    public static Message OutsideArea() => new("Ta lokalizacja jest poza Krakowem. Zgłoszenia przyjmujemy tylko z Krakowa i okolic.", RemoveKeyboard());

    public static Message RateLimited() => new("Wysłano już 5 zgłoszeń w ciągu godziny. Spróbuj później.", RemoveKeyboard());

    public static Message BotReportCreated(CitizenReport r, bool merged) => new(
        merged
            ? $"Dziękujemy! To miejsce zostało już zgłoszone ({Label(r.Type)}), dodaliśmy Twoje potwierdzenie."
            : $"Zgłoszenie przyjęte: {Label(r.Type)}.\nStatus: niezweryfikowane. Pracownicy miasta zobaczą je na mapie. Nagranie nie jest przechowywane.",
        RemoveKeyboard());

    public static Message ReportFailed() => new("Nie udało się zapisać zgłoszenia. Spróbuj ponownie za chwilę.", RemoveKeyboard());

    // ── Helpers ──────────────────────────────────────────────────────────────
    /// <summary>Rounds to 3 decimals (about 100 m), so a message never pinpoints where a resident stood.</summary>
    public static (double Lat, double Lon) Rounded(GeoPoint p) => (Math.Round(p.Latitude, 3), Math.Round(p.Longitude, 3));

    public static string MapUrl(GeoPoint p)
    {
        var (lat, lon) = Rounded(p);
        var la = lat.ToString("0.000", CultureInfo.InvariantCulture);
        var lo = lon.ToString("0.000", CultureInfo.InvariantCulture);
        return $"https://www.openstreetmap.org/?mlat={la}&mlon={lo}#map=17/{la}/{lo}";
    }

    private static string Footer(string? publicBaseUrl) =>
        string.IsNullOrWhiteSpace(publicBaseUrl) ? "Kompas Krakowa" : $"Kompas Krakowa · {publicBaseUrl.TrimEnd('/')}/safety/";
}
