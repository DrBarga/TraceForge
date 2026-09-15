namespace TraceForge.Application.Models;

public sealed record DiagnosticAnomaly(
    string Code,
    string Severity,
    uint Pid,
    string ProcessName,
    string Message);
