using TraceForge.Application.Models;

namespace TraceForge.Application.Diagnostics;

public sealed class DiagnosticAnalyzer
{
    private const double HighCpuThreshold = 85.0;
    private const ulong HighMemoryThreshold = 2UL * 1024 * 1024 * 1024;

    public IReadOnlyList<DiagnosticAnomaly> Analyze(ProcessSnapshot snapshot)
    {
        var anomalies = new List<DiagnosticAnomaly>();

        foreach (var process in snapshot.Processes
                     .Where(process => process.CpuPercent >= HighCpuThreshold)
                     .OrderByDescending(process => process.CpuPercent)
                     .Take(5))
        {
            anomalies.Add(new DiagnosticAnomaly(
                "high_cpu",
                "warning",
                process.Pid,
                process.Name,
                $"CPU usage is {process.CpuPercent:F1}%"));
        }

        foreach (var process in snapshot.Processes
                     .Where(process => process.WorkingSetBytes >= HighMemoryThreshold)
                     .OrderByDescending(process => process.WorkingSetBytes)
                     .Take(5))
        {
            anomalies.Add(new DiagnosticAnomaly(
                "high_memory",
                "warning",
                process.Pid,
                process.Name,
                $"Working set is {FormatBytes(process.WorkingSetBytes)}"));
        }

        return anomalies;
    }

    private static string FormatBytes(ulong bytes)
    {
        var gib = bytes / 1024d / 1024d / 1024d;
        return $"{gib:F2} GiB";
    }
}
