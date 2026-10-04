using KrakowOpenData.Application.Safety;
using KrakowOpenData.Contracts;
using KrakowOpenData.Infrastructure.Ai;
using KrakowOpenData.Infrastructure.Telegram;
using Microsoft.AspNetCore.Http.Features;

namespace KrakowOpenData.Api.Endpoints;

/// <summary>
/// Telegram notifications (link / unlink a resident's app) and voice input for reports. Both are optional features: without
/// Telegram:BotToken + Telegram:BotUsername the link endpoints answer 503, without Ai:BaseUrl the voice endpoint does.
/// Mapped inside the /api/safety group, so validation errors become 400 problems like every other safety endpoint.
/// </summary>
public static class NotificationEndpoints
{
    private static readonly string[] AudioTypes = ["audio/webm", "audio/ogg", "audio/mpeg", "audio/mp3", "audio/mp4", "audio/aac", "audio/wav", "audio/x-wav"];

    public static RouteGroupBuilder MapNotificationEndpoints(this RouteGroupBuilder g)
    {
        g.MapGet("/telegram/link", (string deviceId, TelegramLinkService links) =>
            Results.Ok(new TelegramStatusDto(links.Available, links.IsLinked(deviceId) && links.Available)))
            .WithName("GetTelegramLink")
            .WithSummary("Whether Telegram notifications are available on this server and whether this device is linked.")
            .Produces<TelegramStatusDto>()
            .ProducesValidationProblem();

        g.MapPost("/telegram/link", (TelegramLinkRequest request, TelegramLinkService links) =>
            links.CreateCode(request.DeviceId, request.Latitude, request.Longitude) is { } code
                ? Results.Ok(new TelegramLinkDto(code.Link, code.ExpiresAt))
                : Unavailable("Telegram notifications are not configured on this server."))
            .WithName("CreateTelegramLink")
            .WithSummary("A one-time t.me link (valid 15 minutes) that links this device to the resident's Telegram chat. The optional point is \"my area\": alerts covering it are sent even when the app is closed.")
            .Produces<TelegramLinkDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        g.MapDelete("/telegram/link", (string deviceId, TelegramLinkService links) =>
            links.Unlink(deviceId) ? Results.NoContent() : Results.NotFound())
            .WithName("DeleteTelegramLink")
            .WithSummary("Stops Telegram notifications for this device.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        g.MapPost("/voice", async (HttpRequest http, string deviceId, IVoiceTranscriber transcriber, VoiceQuota quota, CancellationToken ct) =>
        {
            var device = TelegramLinkService.CleanDevice(deviceId);
            if (!transcriber.IsAvailable) return Unavailable("Voice input is not available right now.");

            var contentType = (http.ContentType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();
            if (!AudioTypes.Contains(contentType))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["contentType"] = [$"Send audio as one of: {string.Join(", ", AudioTypes)}."] });
            if (http.ContentLength > WorkersAiClient.MaxAudioBytes) return TooLarge();

            // Kestrel may cap request bodies well below a voice note; lift the cap for this request only, before reading.
            if (http.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodyLimit)
                bodyLimit.MaxRequestBodySize = WorkersAiClient.MaxAudioBytes + 1;

            var audio = await ReadLimitedAsync(http.Body, WorkersAiClient.MaxAudioBytes, ct);
            if (audio is null) return TooLarge();
            if (audio.Length == 0) return Results.ValidationProblem(new Dictionary<string, string[]> { ["body"] = ["The recording is empty."] });
            if (!quota.TryTake(device))
                return Results.Problem(title: "Too many recordings", detail: "Please try again later or type the note.", statusCode: StatusCodes.Status429TooManyRequests);

            // The audio only passes through: it is sent to the Worker and dropped.
            var transcript = await transcriber.TranscribeAsync(audio, contentType, ct);
            if (transcript is null) return Results.Problem(title: "Voice input failed", detail: "The recording could not be recognised.", statusCode: StatusCodes.Status502BadGateway);
            var ai = transcript.Triage is { IsAbuse: false } t ? t : null;
            return Results.Ok(new VoiceTranscriptDto(
                transcript.Text.Length > ReportService.MaxNoteLength ? transcript.Text[..ReportService.MaxNoteLength] : transcript.Text,
                transcript.Language, ai?.SuggestedType, ai?.Severity, ai?.SummaryIsShareable == true ? ai.SummaryPl : null));
        })
            .WithName("TranscribeVoiceNote")
            .WithSummary("Speech to text for a report note (for people who cannot type easily). Body: audio up to 1 MB / 60 s (webm, ogg, mp3, mp4, wav). Returns the text (max 200 characters) and the AI-suggested category, which the resident can change. The audio is not stored.")
            .Accepts<byte[]>("audio/webm", "audio/ogg", "audio/mpeg", "audio/mp4", "audio/wav")
            .Produces<VoiceTranscriptDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return g;
    }

    private static IResult Unavailable(string detail) =>
        Results.Problem(title: "Not available", detail: detail, statusCode: StatusCodes.Status503ServiceUnavailable);

    private static IResult TooLarge() =>
        Results.Problem(title: "Recording too large", detail: "Send at most 1 MB (about 60 seconds).", statusCode: StatusCodes.Status413PayloadTooLarge);

    private static async Task<byte[]?> ReadLimitedAsync(Stream body, int max, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await body.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > max) return null;
        }

        return buffer.ToArray();
    }
}
