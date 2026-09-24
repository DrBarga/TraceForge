namespace TraceForge.Application.Models;

public sealed record DiagnosticIncident(
    string ProcessName,
    ProcessExit Exit,
    IReadOnlyList<ProcessSnapshot> Timeline,
    long StartTimeUnixMs = 0);
