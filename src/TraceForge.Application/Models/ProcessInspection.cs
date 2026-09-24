namespace TraceForge.Application.Models;

public sealed record ThreadDetails(uint Id, int BasePriority);

public sealed record ModuleDetails(string Name, string Path, uint SizeBytes);

public sealed record ConnectionDetails(
    string Protocol,
    string LocalAddress,
    ushort LocalPort,
    string RemoteAddress,
    ushort RemotePort,
    string State);

public sealed record ProcessInspection(
    uint Pid,
    long StartTimeUnixMs,
    IReadOnlyList<ThreadDetails> Threads,
    IReadOnlyList<ModuleDetails> Modules,
    IReadOnlyList<ConnectionDetails> Connections,
    uint ThreadError,
    uint ModuleError,
    uint NetworkError,
    uint IdentityError);
