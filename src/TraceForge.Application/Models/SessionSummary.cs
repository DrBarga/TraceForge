namespace TraceForge.Application.Models;

public sealed record SessionSummary(
    long Id,
    DateTimeOffset StartedUtc,
    DateTimeOffset? EndedUtc,
    int SnapshotCount,
    int IncidentCount);

public sealed record IncidentSummary(
    long SessionId,
    uint Pid,
    string ProcessName,
    uint ExitCode,
    DateTimeOffset ObservedUtc);
