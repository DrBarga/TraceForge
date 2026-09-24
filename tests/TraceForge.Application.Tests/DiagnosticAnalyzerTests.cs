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
            [Process(42, "test.exe", 91.2, 1000)]);

        var anomalies = new DiagnosticAnalyzer().Analyze(snapshot);

        Assert.Contains(anomalies, anomaly => anomaly.Code == "high_cpu" && anomaly.Pid == 42);
    }

    [Fact]
    public void Analyze_ReportsHighMemoryProcess()
    {
        var snapshot = new ProcessSnapshot(
            DateTimeOffset.UtcNow,
            [Process(77, "memory.exe", 1.0, 3UL * 1024 * 1024 * 1024)]);

        var anomalies = new DiagnosticAnalyzer().Analyze(snapshot);

        Assert.Contains(anomalies, anomaly => anomaly.Code == "high_memory" && anomaly.Pid == 77);
    }

    [Fact]
    public void Analyze_DoesNotTreatUnavailableMetricsAsRealValues()
    {
        var process = Process(99, "protected.exe", 99.0, 4UL * 1024 * 1024 * 1024) with
        {
            CpuAvailable = false,
            MemoryAvailable = false
        };
        var snapshot = new ProcessSnapshot(DateTimeOffset.UtcNow, [process]);

        var anomalies = new DiagnosticAnalyzer().Analyze(snapshot);

        Assert.Empty(anomalies);
    }

    [Fact]
    public void Analyze_IncludesThresholdBoundary()
    {
        var snapshot = new ProcessSnapshot(
            DateTimeOffset.UtcNow,
            [Process(100, "boundary.exe", 85.0, 2UL * 1024 * 1024 * 1024)]);

        var anomalies = new DiagnosticAnalyzer().Analyze(snapshot);

        Assert.Contains(anomalies, anomaly => anomaly.Code == "high_cpu");
        Assert.Contains(anomalies, anomaly => anomaly.Code == "high_memory");
    }

    private static ProcessInfo Process(uint pid, string name, double cpu, ulong memory)
    {
        return new ProcessInfo(
            pid,
            1,
            name,
            $"C:\\{name}",
            cpu,
            memory,
            4,
            true,
            0,
            true,
            0,
            true,
            0,
            true,
            0);
    }
}
