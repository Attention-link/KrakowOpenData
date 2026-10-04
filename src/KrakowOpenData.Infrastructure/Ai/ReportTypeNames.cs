using KrakowOpenData.Domain.Safety;

namespace KrakowOpenData.Infrastructure.Ai;

/// <summary>Reads a report type that came from outside (a model, a button) by NAME only: "3" or "LightOut, HeatSpot" are rejected.</summary>
public static class ReportTypeNames
{
    public static ReportType? Parse(string? value)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        var name = Enum.GetNames<ReportType>().FirstOrDefault(n => n.Equals(v, StringComparison.OrdinalIgnoreCase));
        return name is null ? null : Enum.Parse<ReportType>(name);
    }
}
