namespace TraceForge.Application.Models;

public sealed record ProcessSnapshot(
    DateTimeOffset TimestampUtc,
    IReadOnlyList<ProcessInfo> Processes,
    ProcessExit? ProcessExit = null);
