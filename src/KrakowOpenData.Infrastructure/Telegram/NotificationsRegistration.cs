using KrakowOpenData.Application.Safety;
using KrakowOpenData.Infrastructure.Ai;
using KrakowOpenData.Infrastructure.Options;
using KrakowOpenData.Infrastructure.Safety;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KrakowOpenData.Infrastructure.Telegram;

/// <summary>
/// Safety events, AI (Cloudflare Workers AI through workers/ai) and the Telegram bot. All of it is a no-op without configuration:
/// no Telegram:BotToken = nothing is sent or polled; no Ai:BaseUrl = no triage and no voice input.
/// </summary>
public static class NotificationsRegistration
{
    /// <summary>Cloudflare in front of opendata.al.mt answers 403 to default library User-Agents, so every server-side client sets one.</summary>
    public const string UserAgent = "KompasKrakowa-api/1.0";

    public static IServiceCollection AddSafetyNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TelegramOptions>(configuration.GetSection(TelegramOptions.SectionName));
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));

        // ── AI (Workers AI) ──
        services.AddHttpClient(WorkersAiClient.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60); // per-call limits are shorter (Ai:TimeoutSeconds, Ai:TranscribeTimeoutSeconds)
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        });
        services.AddSingleton<WorkersAiClient>();
        services.AddSingleton<IReportTriage>(sp => sp.GetRequiredService<WorkersAiClient>());
        services.AddSingleton<IVoiceTranscriber>(sp => sp.GetRequiredService<WorkersAiClient>());
        services.AddSingleton<VoiceQuota>();

        // ── Telegram ──
        services.AddHttpClient(TelegramClient.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(TelegramClient.PollSeconds + 20); // long polling; other calls cap themselves at 10 s
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        });
        services.AddSingleton<TelegramClient>();
        services.AddSingleton<TelegramStore>();
        services.AddSingleton<TelegramOutbox>();
        services.AddHostedService(sp => sp.GetRequiredService<TelegramOutbox>());
        services.AddSingleton<TelegramNotifier>();
        services.AddSingleton<TelegramLinkService>();
        services.AddSingleton<TelegramBotService>();
        services.AddHostedService(sp => sp.GetRequiredService<TelegramBotService>());

        // ── Safety events: bounded queue + one consumer (triage, then notifications) ──
        services.AddSingleton<SafetyEventPipeline>();
        services.AddSingleton<ISafetyEventSink>(sp => sp.GetRequiredService<SafetyEventPipeline>());
        services.AddHostedService(sp => sp.GetRequiredService<SafetyEventPipeline>());
        return services;
    }
}
