using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KrakowOpenData.Infrastructure.Telegram;

public enum LinkStatus
{
    Connected,

    /// <summary>The chat blocked the bot or no longer exists; nothing is sent until the resident links again.</summary>
    NotReceiving
}

/// <summary>
/// A resident's app (anonymous random device id) linked to their own Telegram chat. <see cref="Area"/> is the "my area" point the
/// app had when linking, rounded to 3 decimals (about 100 m); alerts covering it are sent even when the app is closed.
/// </summary>
public sealed record TelegramLink(string DeviceId, long ChatId, GeoPoint? Area, DateTimeOffset LinkedAt, LinkStatus Status, DateTimeOffset StatusAt);

/// <summary>A one-time link code. Only its SHA-256 is stored (as in SafeWalk's contacts.py); the code itself exists only in the deep link.</summary>
public sealed record TelegramLinkCode(string CodeHash, string DeviceId, GeoPoint? Area, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? UsedAt);

public enum OutboxState
{
    Pending,
    Sent,
    Failed
}

/// <summary>One message to send. Only the outbox loop sends, so a message is never sent by two callers at once.</summary>
public sealed record OutboxMessage(
    long Id,
    string ChatId,
    string Kind,
    string Text,
    string? ReplyMarkupJson,
    int Attempts,
    DateTimeOffset FirstAt,
    DateTimeOffset NextAt,
    OutboxState State,
    string? LastError,
    DateTimeOffset? EndedAt);

/// <summary>
/// Links, pending link codes, the outbox and the bot's update offset, kept in memory and saved to one JSON file after every change
/// (the same pattern as <see cref="Safety.JsonFileSafetyStore"/>). Memory only when Safety:Persist is off (tests).
/// Ended outbox rows go after 24 h and used or expired codes after 1 h.
/// </summary>
public sealed class TelegramStore
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private readonly object _lock = new();
    private readonly ILogger<TelegramStore> _logger;
    private readonly string? _path;
    private readonly Dictionary<string, TelegramLink> _links = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TelegramLinkCode> _codes = new(StringComparer.Ordinal);
    private readonly List<OutboxMessage> _outbox = [];
    private long _nextId = 1;
    private long? _offset;

    public TelegramStore(IOptions<TelegramOptions> telegram, IOptions<SafetyOptions> safety, ILogger<TelegramStore> logger)
    {
        _logger = logger;
        if (!safety.Value.Persist) return;
        _path = !string.IsNullOrWhiteSpace(telegram.Value.StorePath)
            ? telegram.Value.StorePath
            : Path.Combine(
                string.IsNullOrWhiteSpace(safety.Value.StorePath)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KrakowOpenData")
                    : Path.GetDirectoryName(Path.GetFullPath(safety.Value.StorePath))!,
                "telegram-store.json");
        Load();
    }

    public static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();

    // ── Codes ────────────────────────────────────────────────────────────────
    /// <summary>Stores a new code; older unused codes of the device are expired so at most one is live.</summary>
    public void AddCode(TelegramLinkCode code)
    {
        lock (_lock)
        {
            foreach (var (key, old) in _codes.ToList())
                if (old.DeviceId == code.DeviceId && old.UsedAt is null && old.ExpiresAt > code.CreatedAt)
                    _codes[key] = old with { ExpiresAt = code.CreatedAt };
            _codes[code.CodeHash] = code;
            Persist();
        }
    }

    public enum AcceptResult { Ok, Used, Expired, Unknown }

    /// <summary>Uses a code (once) and links its device to the chat. Idempotent for a chat that is already linked.</summary>
    public (AcceptResult Result, TelegramLink? Link) AcceptCode(string code, long chatId, DateTimeOffset now)
    {
        lock (_lock)
        {
            if (!_codes.TryGetValue(Hash(code), out var c)) return (AcceptResult.Unknown, null);
            if (c.UsedAt is not null) return (AcceptResult.Used, null);
            if (c.ExpiresAt <= now) return (AcceptResult.Expired, null);
            _codes[c.CodeHash] = c with { UsedAt = now };
            var link = new TelegramLink(c.DeviceId, chatId, c.Area, now, LinkStatus.Connected, now);
            _links[c.DeviceId] = link;
            Persist();
            return (AcceptResult.Ok, link);
        }
    }

    // ── Links ────────────────────────────────────────────────────────────────
    public TelegramLink? LinkOf(string deviceId)
    {
        lock (_lock) return _links.GetValueOrDefault(deviceId);
    }

    public IReadOnlyList<TelegramLink> ConnectedLinks()
    {
        lock (_lock) return _links.Values.Where(l => l.Status == LinkStatus.Connected).ToList();
    }

    /// <summary>Links a device to a chat directly (reports filed through the bot, so their status changes reach the same chat).</summary>
    public void UpsertLink(TelegramLink link)
    {
        lock (_lock)
        {
            _links[link.DeviceId] = link;
            Persist();
        }
    }

    public TelegramLink? RemoveLink(string deviceId)
    {
        lock (_lock)
        {
            if (!_links.Remove(deviceId, out var link)) return null;
            // Messages not yet sent to this chat about this device stay queued only if another device still uses the chat.
            if (!_links.Values.Any(l => l.ChatId == link.ChatId)) DropPending(link.ChatId.ToString());
            Persist();
            return link;
        }
    }

    /// <summary>/stop: unlinks every device of the chat. Returns how many.</summary>
    public int RemoveChat(long chatId)
    {
        lock (_lock)
        {
            var devices = _links.Values.Where(l => l.ChatId == chatId).Select(l => l.DeviceId).ToList();
            foreach (var d in devices) _links.Remove(d);
            if (devices.Count > 0) Persist();
            return devices.Count;
        }
    }

    /// <summary>The chat blocked the bot or does not exist: stop sending to it.</summary>
    public int MarkNotReceiving(long chatId, DateTimeOffset now)
    {
        lock (_lock)
        {
            var n = 0;
            foreach (var l in _links.Values.Where(l => l.ChatId == chatId && l.Status == LinkStatus.Connected).ToList())
            {
                _links[l.DeviceId] = l with { Status = LinkStatus.NotReceiving, StatusAt = now };
                n++;
            }

            if (n > 0)
            {
                DropPending(chatId.ToString());
                Persist();
            }

            return n;
        }
    }

    // ── Outbox ───────────────────────────────────────────────────────────────
    public OutboxMessage Enqueue(string chatId, string kind, string text, string? replyMarkupJson, DateTimeOffset now)
    {
        lock (_lock)
        {
            var m = new OutboxMessage(_nextId++, chatId, kind, text, replyMarkupJson, 0, now, now, OutboxState.Pending, null, null);
            _outbox.Add(m);
            Persist();
            return m;
        }
    }

    public IReadOnlyList<OutboxMessage> Due(DateTimeOffset now)
    {
        lock (_lock) return _outbox.Where(m => m.State == OutboxState.Pending && m.NextAt <= now).OrderBy(m => m.Id).ToList();
    }

    public bool IsPending(long id)
    {
        lock (_lock) return _outbox.Any(m => m.Id == id && m.State == OutboxState.Pending);
    }

    public IReadOnlyList<OutboxMessage> Outbox()
    {
        lock (_lock) return _outbox.ToList();
    }

    /// <summary>Replaces a still-pending row (a row dropped meanwhile, e.g. after unlinking, stays dropped).</summary>
    public void Update(OutboxMessage message)
    {
        lock (_lock)
        {
            var i = _outbox.FindIndex(m => m.Id == message.Id && m.State == OutboxState.Pending);
            if (i < 0) return;
            _outbox[i] = message;
            Persist();
        }
    }

    // ── Bot offset ───────────────────────────────────────────────────────────
    public long? Offset
    {
        get { lock (_lock) return _offset; }
    }

    public void SaveOffset(long offset)
    {
        lock (_lock)
        {
            _offset = offset;
            Persist();
        }
    }

    /// <summary>Retention: ended messages after 24 h, used or expired codes after 1 h.</summary>
    public void Housekeeping(DateTimeOffset now)
    {
        lock (_lock)
        {
            var removed = _outbox.RemoveAll(m => m.State != OutboxState.Pending && (m.EndedAt ?? m.NextAt) < now.AddHours(-24));
            foreach (var (key, c) in _codes.ToList())
                if (c.ExpiresAt < now.AddHours(-1) || (c.UsedAt is { } u && u < now.AddHours(-1))) { _codes.Remove(key); removed++; }
            if (removed > 0) Persist();
        }
    }

    private void DropPending(string chatId) =>
        _outbox.RemoveAll(m => m.State == OutboxState.Pending && m.ChatId == chatId && m.Kind != TelegramMessages.Kinds.Unlinked);

    private sealed record Snapshot(
        List<TelegramLink> Links, List<TelegramLinkCode> Codes, List<OutboxMessage> Outbox, long NextId, long? Offset);

    private void Load()
    {
        try
        {
            if (_path is null || !File.Exists(_path)) return;
            var s = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(_path), Json);
            if (s is null) return;
            foreach (var l in s.Links) _links[l.DeviceId] = l;
            foreach (var c in s.Codes) _codes[c.CodeHash] = c;
            _outbox.AddRange(s.Outbox);
            _nextId = Math.Max(s.NextId, _outbox.Count == 0 ? 1 : _outbox.Max(m => m.Id) + 1);
            _offset = s.Offset;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning("Could not read the Telegram store ({Error}); starting empty", ex.GetType().Name);
        }
    }

    /// <summary>Called inside the lock. Failures are logged: the data stays in memory.</summary>
    private void Persist()
    {
        if (_path is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new Snapshot(_links.Values.ToList(), _codes.Values.ToList(), _outbox, _nextId, _offset), Json));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Could not save the Telegram store ({Error})", ex.GetType().Name);
        }
    }
}
