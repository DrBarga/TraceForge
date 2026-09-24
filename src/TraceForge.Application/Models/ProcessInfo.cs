namespace TraceForge.Application.Models;

public sealed record ProcessInfo(
    uint Pid,
    uint ParentPid,
    string Name,
    string Path,
    double CpuPercent,
    ulong WorkingSetBytes,
    uint ThreadCount,
    bool Accessible,
    uint AccessError,
    bool PathAvailable,
    uint PathError,
    bool CpuAvailable,
    uint CpuError,
    bool MemoryAvailable,
    uint MemoryError,
    long StartTimeUnixMs = 0);
