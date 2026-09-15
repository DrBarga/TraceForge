namespace TraceForge.Application.Models;

public sealed record ProcessInfo(
    uint Pid,
    string Name,
    string Path,
    double CpuPercent,
    ulong WorkingSetBytes,
    uint ThreadCount,
    bool Accessible,
    uint AccessError);
