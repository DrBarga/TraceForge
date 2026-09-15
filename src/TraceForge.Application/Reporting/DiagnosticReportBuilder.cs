using System.Text.Json;
using TraceForge.Application.Models;

namespace TraceForge.Application.Reporting;

public sealed class DiagnosticReportBuilder
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string Build(
        ProcessSnapshot current,
        IReadOnlyList<ProcessSnapshot> blackBox,
        IReadOnlyList<DiagnosticAnomaly> anomalies,
        string version)
    {
        var report = new
        {
            schemaVersion = 1,
            generatedUtc = DateTimeOffset.UtcNow,
            traceForgeVersion = version,
            current,
            anomalies,
            blackBox
        };

        return JsonSerializer.Serialize(report, Options);
    }
}
