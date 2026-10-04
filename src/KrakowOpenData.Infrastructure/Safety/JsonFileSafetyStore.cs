using System.Text.Json;
using System.Text.Json.Serialization;
using KrakowOpenData.Application.Safety;
using KrakowOpenData.Domain.Safety;
using KrakowOpenData.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KrakowOpenData.Infrastructure.Safety;

/// <summary>
/// Citizen reports, planner alerts and agency dispatches kept in memory and saved to one JSON file after every change,
/// so the demo survives a restart. With <see cref="SafetyOptions.Persist"/> off it is memory only (tests).
/// A few thousand records are fine; use a database for more.
/// </summary>
public sealed class JsonFileSafetyStore : ISafetyStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
        IncludeFields = false
    };

    private readonly object _lock = new();
    private readonly ILogger<JsonFileSafetyStore> _logger;
    private readonly string? _path;
    private readonly Dictionary<string, CitizenReport> _reports = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PlannerAlert> _alerts = new(StringComparer.Ordinal);
    private readonly List<AgencyDispatch> _dispatches = [];
    private Dictionary<string, double> _weights = new(StringComparer.Ordinal);

    public JsonFileSafetyStore(IOptions<SafetyOptions> options, ILogger<JsonFileSafetyStore> logger)
    {
        _logger = logger;
        if (!options.Value.Persist) return;

        _path = string.IsNullOrWhiteSpace(options.Value.StorePath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KrakowOpenData", "safety-store.json")
            : options.Value.StorePath;
        Load();
    }

    public Task<IReadOnlyList<CitizenReport>> ListReportsAsync(CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult<IReadOnlyList<CitizenReport>>(_reports.Values.ToList());
    }

    public Task<CitizenReport?> GetReportAsync(string id, CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult(_reports.GetValueOrDefault(id));
    }

    public Task SaveReportAsync(CitizenReport report, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _reports[report.Id] = report;
            Persist();
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PlannerAlert>> ListAlertsAsync(CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult<IReadOnlyList<PlannerAlert>>(_alerts.Values.ToList());
    }

    public Task<PlannerAlert?> GetAlertAsync(string id, CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult(_alerts.GetValueOrDefault(id));
    }

    public Task SaveAlertAsync(PlannerAlert alert, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _alerts[alert.Id] = alert;
            Persist();
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AgencyDispatch>> ListDispatchesAsync(CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult<IReadOnlyList<AgencyDispatch>>(_dispatches.ToList());
    }

    public Task AddDispatchAsync(AgencyDispatch dispatch, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _dispatches.Add(dispatch);
            Persist();
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, double>> GetWeightOverridesAsync(CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult<IReadOnlyDictionary<string, double>>(new Dictionary<string, double>(_weights, StringComparer.Ordinal));
    }

    public Task SaveWeightOverridesAsync(IReadOnlyDictionary<string, double> weights, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _weights = new Dictionary<string, double>(weights, StringComparer.Ordinal);
            Persist();
        }

        return Task.CompletedTask;
    }

    private sealed record Snapshot(List<CitizenReport> Reports, List<PlannerAlert> Alerts, List<AgencyDispatch> Dispatches, Dictionary<string, double>? Weights = null);

    private void Load()
    {
        try
        {
            if (_path is null || !File.Exists(_path)) return;
            var snapshot = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(_path), Json);
            if (snapshot is null) return;
            foreach (var r in snapshot.Reports) _reports[r.Id] = r;
            foreach (var a in snapshot.Alerts) _alerts[a.Id] = a;
            _dispatches.AddRange(snapshot.Dispatches);
            if (snapshot.Weights is not null) _weights = new Dictionary<string, double>(snapshot.Weights, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "Could not read the safety store; starting empty");
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
            File.WriteAllText(temp, JsonSerializer.Serialize(new Snapshot(_reports.Values.ToList(), _alerts.Values.ToList(), _dispatches, _weights), Json));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not save the safety store");
        }
    }
}
