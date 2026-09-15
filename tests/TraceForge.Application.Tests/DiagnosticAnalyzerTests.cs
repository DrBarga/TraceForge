using Xunit;
using TraceForge.Application.Diagnostics;
using TraceForge.Application.Models;

namespace TraceForge.Application.Tests;

public sealed class DiagnosticAnalyzerTests
{
    [Fact]
    public void Analyze_ReportsHighCpuProcess()
    {
        var snapshot = new ProcessSnapshot(
            DateTimeOffset.UtcNow,
            [new ProcessInfo(42, "test.exe", "C:\\test.exe", 91.2, 1000, 4, true, 0)]);

        var anomalies = new DiagnosticAnalyzer().Analyze(snapshot);

        Assert.Contains(anomalies, anomaly => anomaly.Code == "high_cpu" && anomaly.Pid == 42);
    }

    [Fact]
    public void Analyze_ReportsHighMemoryProcess()
    {
        var snapshot = new ProcessSnapshot(
            DateTimeOffset.UtcNow,
            [new ProcessInfo(77, "memory.exe", "C:\\memory.exe", 1.0, 3UL * 1024 * 1024 * 1024, 8, true, 0)]);

        var anomalies = new DiagnosticAnalyzer().Analyze(snapshot);

        Assert.Contains(anomalies, anomaly => anomaly.Code == "high_memory" && anomaly.Pid == 77);
    }
}
