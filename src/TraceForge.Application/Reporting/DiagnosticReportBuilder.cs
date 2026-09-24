using System.Text.Json;
using System.Text;
using System.Net;
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
        string version,
        DiagnosticIncident? incident = null,
        ProcessInspection? inspection = null)
    {
        var report = new
        {
            schemaVersion = 3,
            generatedUtc = DateTimeOffset.UtcNow,
            traceForgeVersion = version,
            current,
            anomalies,
            blackBox = incident?.Timeline ?? blackBox,
            incident,
            inspection
        };

        return JsonSerializer.Serialize(report, Options);
    }

    public string BuildHtml(
        ProcessSnapshot current,
        IReadOnlyList<ProcessSnapshot> blackBox,
        IReadOnlyList<DiagnosticAnomaly> anomalies,
        string version,
        DiagnosticIncident? incident = null,
        ProcessInspection? inspection = null)
    {
        var generatedUtc = DateTimeOffset.UtcNow;
        var topCpu = current.Processes
            .Where(process => process.CpuAvailable)
            .OrderByDescending(process => process.CpuPercent)
            .Take(10)
            .ToArray();
        var topMemory = current.Processes
            .Where(process => process.MemoryAvailable)
            .OrderByDescending(process => process.WorkingSetBytes)
            .Take(10)
            .ToArray();

        var html = new StringBuilder(16 * 1024);
        html.Append("""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>TraceForge diagnostic report</title>
              <style>
                :root { color-scheme: light dark; font-family: "Segoe UI", sans-serif; }
                body { max-width: 1120px; margin: 0 auto; padding: 32px; line-height: 1.45; }
                h1, h2 { margin: 0 0 12px; }
                h2 { margin-top: 32px; }
                .meta { color: #667085; margin-bottom: 24px; }
                .summary { display: grid; grid-template-columns: repeat(auto-fit, minmax(160px, 1fr)); gap: 12px; }
                .metric { border: 1px solid #d0d5dd; border-radius: 8px; padding: 14px; }
                .metric strong { display: block; font-size: 1.5rem; }
                table { width: 100%; border-collapse: collapse; font-size: 0.92rem; }
                th, td { padding: 9px 10px; border-bottom: 1px solid #d0d5dd; text-align: left; }
                th { background: #f2f4f7; color: #101828; }
                .number { text-align: right; font-variant-numeric: tabular-nums; }
                .empty { color: #667085; }
                @media (prefers-color-scheme: dark) { th { background: #1d2939; color: #f9fafb; } }
              </style>
            </head>
            <body>
            """);
        html.Append("<h1>TraceForge diagnostic report</h1>");
        html.Append("<div class=\"meta\">Version ")
            .Append(Encode(version))
            .Append(" · Generated ")
            .Append(Encode(generatedUtc.ToString("u")))
            .Append(" · Snapshot ")
            .Append(Encode(current.TimestampUtc.ToString("u")))
            .Append("</div>");

        html.Append("<div class=\"summary\">")
            .Append(Metric("Processes", current.Processes.Count.ToString()))
            .Append(Metric("Anomalies", anomalies.Count.ToString()))
            .Append(Metric("Limited access", current.Processes.Count(process => !process.Accessible || !process.MemoryAvailable).ToString()))
            .Append(Metric("Black Box samples", (incident?.Timeline ?? blackBox).Count.ToString()))
            .Append("</div>");

        AppendIncident(html, incident);
        if (incident is not null)
        {
            AppendWatchedProcessTimeline(html, incident);
        }
        AppendInspection(html, inspection);
        AppendAnomalies(html, anomalies);
        AppendProcessTable(html, "Highest CPU usage", topCpu);
        AppendProcessTable(html, "Largest working sets", topMemory);
        AppendTimeline(html, incident?.Timeline ?? blackBox);

        html.Append("</body></html>");
        return html.ToString();
    }

    private static void AppendInspection(StringBuilder html, ProcessInspection? inspection)
    {
        if (inspection is null)
        {
            return;
        }

        html.Append("<h2>Process inspection · PID ").Append(inspection.Pid).Append("</h2>");
        html.Append("<p>").Append(inspection.Threads.Count).Append(" threads · ")
            .Append(inspection.Modules.Count).Append(" modules · ")
            .Append(inspection.Connections.Count).Append(" network endpoints</p>");

        if (inspection.ThreadError != 0 || inspection.ModuleError != 0 ||
            inspection.NetworkError != 0 || inspection.IdentityError != 0)
        {
            html.Append("<p class=\"empty\">Some inspection data could not be read. Win32 errors: identity ")
                .Append(inspection.IdentityError).Append(", threads ")
                .Append(inspection.ThreadError).Append(", modules ")
                .Append(inspection.ModuleError).Append(", network ")
                .Append(inspection.NetworkError).Append(".</p>");
        }

        if (inspection.Connections.Count > 0)
        {
            html.Append("<h3>Network endpoints</h3><table><thead><tr><th>Protocol</th><th>Local</th><th>Remote</th><th>State</th></tr></thead><tbody>");
            foreach (var connection in inspection.Connections)
            {
                html.Append("<tr><td>").Append(Encode(connection.Protocol))
                    .Append("</td><td>").Append(Encode($"{connection.LocalAddress}:{connection.LocalPort}"))
                    .Append("</td><td>").Append(Encode(string.IsNullOrEmpty(connection.RemoteAddress)
                        ? "—" : $"{connection.RemoteAddress}:{connection.RemotePort}"))
                    .Append("</td><td>").Append(Encode(connection.State)).Append("</td></tr>");
            }
            html.Append("</tbody></table>");
        }

        if (inspection.Modules.Count > 0)
        {
            html.Append("<h3>Loaded modules</h3><table><thead><tr><th>Module</th><th>Path</th><th>Size</th></tr></thead><tbody>");
            foreach (var module in inspection.Modules)
            {
                html.Append("<tr><td>").Append(Encode(module.Name))
                    .Append("</td><td>").Append(Encode(module.Path))
                    .Append("</td><td class=\"number\">").Append(FormatBytes(module.SizeBytes))
                    .Append("</td></tr>");
            }
            html.Append("</tbody></table>");
        }
    }

    private static void AppendIncident(StringBuilder html, DiagnosticIncident? incident)
    {
        html.Append("<h2>Watched process</h2>");
        if (incident is null)
        {
            html.Append("<p class=\"empty\">No watched process exit has been recorded.</p>");
            return;
        }

        html.Append("<p><strong>").Append(Encode(incident.ProcessName))
            .Append("</strong> (PID ").Append(incident.Exit.Pid)
            .Append(") exited at ").Append(Encode(incident.Exit.TimestampUtc.ToLocalTime().ToString("u")))
            .Append(" with code <code>0x").Append(incident.Exit.ExitCode.ToString("X8"))
            .Append("</code>. ");

        html.Append(incident.Exit.HasExceptionStatus
            ? "The code is in the Windows exception status range; confirm the cause with the application log or a dump."
            : "An exit code alone does not establish whether the application crashed.");
        html.Append("</p>");
    }

    private static void AppendWatchedProcessTimeline(StringBuilder html, DiagnosticIncident incident)
    {
        html.Append("<h2>Watched process timeline</h2>");
        var samples = incident.Timeline
            .Select(snapshot => new
            {
                snapshot.TimestampUtc,
                Process = snapshot.Processes.FirstOrDefault(process =>
                    process.Pid == incident.Exit.Pid &&
                    (incident.StartTimeUnixMs == 0 ||
                     process.StartTimeUnixMs == incident.StartTimeUnixMs))
            })
            .Where(sample => sample.Process is not null)
            .ToArray();

        if (samples.Length == 0)
        {
            html.Append("<p class=\"empty\">No samples for the watched process are available.</p>");
            return;
        }

        html.Append("<table><thead><tr><th>Time</th><th>CPU</th><th>Working set</th><th>Threads</th></tr></thead><tbody>");
        foreach (var sample in samples)
        {
            var process = sample.Process!;
            html.Append("<tr><td>").Append(Encode(sample.TimestampUtc.ToLocalTime().ToString("HH:mm:ss")))
                .Append("</td><td class=\"number\">")
                .Append(process.CpuAvailable ? $"{process.CpuPercent:F1}%" : "Unavailable")
                .Append("</td><td class=\"number\">")
                .Append(process.MemoryAvailable ? FormatBytes(process.WorkingSetBytes) : "Unavailable")
                .Append("</td><td class=\"number\">").Append(process.ThreadCount)
                .Append("</td></tr>");
        }

        html.Append("</tbody></table>");
    }

    private static void AppendAnomalies(StringBuilder html, IReadOnlyList<DiagnosticAnomaly> anomalies)
    {
        html.Append("<h2>Anomalies</h2>");
        if (anomalies.Count == 0)
        {
            html.Append("<p class=\"empty\">No threshold anomalies were detected in the latest snapshot.</p>");
            return;
        }

        html.Append("<table><thead><tr><th>Severity</th><th>Process</th><th>PID</th><th>Finding</th></tr></thead><tbody>");
        foreach (var anomaly in anomalies)
        {
            html.Append("<tr><td>").Append(Encode(anomaly.Severity))
                .Append("</td><td>").Append(Encode(anomaly.ProcessName))
                .Append("</td><td class=\"number\">").Append(anomaly.Pid)
                .Append("</td><td>").Append(Encode(anomaly.Message))
                .Append("</td></tr>");
        }

        html.Append("</tbody></table>");
    }

    private static void AppendProcessTable(
        StringBuilder html,
        string title,
        IReadOnlyList<ProcessInfo> processes)
    {
        html.Append("<h2>").Append(Encode(title)).Append("</h2>");
        if (processes.Count == 0)
        {
            html.Append("<p class=\"empty\">No available measurements.</p>");
            return;
        }

        html.Append("<table><thead><tr><th>Process</th><th>PID</th><th>CPU</th><th>Working set</th><th>Threads</th></tr></thead><tbody>");
        foreach (var process in processes)
        {
            html.Append("<tr><td>").Append(Encode(process.Name))
                .Append("</td><td class=\"number\">").Append(process.Pid)
                .Append("</td><td class=\"number\">")
                .Append(process.CpuAvailable ? $"{process.CpuPercent:F1}%" : "Unavailable")
                .Append("</td><td class=\"number\">")
                .Append(process.MemoryAvailable ? FormatBytes(process.WorkingSetBytes) : "Unavailable")
                .Append("</td><td class=\"number\">").Append(process.ThreadCount)
                .Append("</td></tr>");
        }

        html.Append("</tbody></table>");
    }

    private static void AppendTimeline(StringBuilder html, IReadOnlyList<ProcessSnapshot> blackBox)
    {
        html.Append("<h2>Black Box timeline</h2>");
        if (blackBox.Count == 0)
        {
            html.Append("<p class=\"empty\">No Black Box samples are available.</p>");
            return;
        }

        html.Append("<table><thead><tr><th>Time</th><th>Processes</th><th>Measured CPU</th><th>Measured memory</th></tr></thead><tbody>");
        foreach (var snapshot in blackBox)
        {
            html.Append("<tr><td>").Append(Encode(snapshot.TimestampUtc.ToLocalTime().ToString("HH:mm:ss")))
                .Append("</td><td class=\"number\">").Append(snapshot.Processes.Count)
                .Append("</td><td class=\"number\">").Append(snapshot.Processes.Count(process => process.CpuAvailable))
                .Append("</td><td class=\"number\">").Append(snapshot.Processes.Count(process => process.MemoryAvailable))
                .Append("</td></tr>");
        }

        html.Append("</tbody></table>");
    }

    private static string Metric(string label, string value)
    {
        return $"<div class=\"metric\"><span>{Encode(label)}</span><strong>{Encode(value)}</strong></div>";
    }

    private static string FormatBytes(ulong bytes)
    {
        return bytes >= 1024UL * 1024 * 1024
            ? $"{bytes / 1024d / 1024d / 1024d:F2} GiB"
            : $"{bytes / 1024d / 1024d:F1} MiB";
    }

    private static string Encode(string value)
    {
        return WebUtility.HtmlEncode(value);
    }
}
