using System.Text.Json;
using TraceForge.Application.Models;
using TraceForge.Application.Reporting;
using Xunit;

namespace TraceForge.Application.Tests;

public sealed class DiagnosticReportBuilderTests
{
    [Fact]
    public void Build_WritesStableSchemaAndMetricAvailability()
    {
        var process = new ProcessInfo(
            42,
            1,
            "sample.exe",
            string.Empty,
            0,
            0,
            3,
            true,
            0,
            false,
            5,
            false,
            0,
            false,
            5);
        var snapshot = new ProcessSnapshot(DateTimeOffset.UnixEpoch, [process]);

        var json = new DiagnosticReportBuilder().Build(snapshot, [snapshot], [], "1.2.3");
        using var document = JsonDocument.Parse(json);

        Assert.Equal(3, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("1.2.3", document.RootElement.GetProperty("traceForgeVersion").GetString());

        var serializedProcess = document.RootElement
            .GetProperty("current")
            .GetProperty("processes")[0];
        Assert.False(serializedProcess.GetProperty("pathAvailable").GetBoolean());
        Assert.Equal(5u, serializedProcess.GetProperty("pathError").GetUInt32());
        Assert.False(serializedProcess.GetProperty("memoryAvailable").GetBoolean());
    }

    [Fact]
    public void BuildHtml_EncodesProcessNames()
    {
        var process = new ProcessInfo(
            42,
            1,
            "<sample&process>.exe",
            string.Empty,
            5,
            1024,
            1,
            true,
            0,
            true,
            0,
            true,
            0,
            true,
            0);
        var snapshot = new ProcessSnapshot(DateTimeOffset.UnixEpoch, [process]);

        var html = new DiagnosticReportBuilder().BuildHtml(snapshot, [snapshot], [], "1.2.3");

        Assert.Contains("&lt;sample&amp;process&gt;.exe", html);
        Assert.DoesNotContain("<sample&process>.exe", html);
    }

    [Fact]
    public void BuildHtml_ShowsWatchedProcessMeasurements()
    {
        var process = new ProcessInfo(
            42, 1, "sample.exe", "C:\\sample.exe", 12.5, 1024 * 1024, 3,
            true, 0, true, 0, true, 0, true, 0, 1234);
        var snapshot = new ProcessSnapshot(DateTimeOffset.UnixEpoch, [process]);
        var incident = new DiagnosticIncident(
            "sample.exe", new ProcessExit(42, 1, DateTimeOffset.UnixEpoch), [snapshot], 1234);

        var html = new DiagnosticReportBuilder().BuildHtml(snapshot, [snapshot], [], "1.0.0", incident);

        Assert.Contains("Watched process timeline", html);
        Assert.Contains($"{12.5:F1}%", html);
        Assert.Contains($"{1.0:F1} MiB", html);
    }
}
