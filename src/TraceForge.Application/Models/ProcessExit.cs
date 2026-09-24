namespace TraceForge.Application.Models;

public sealed record ProcessExit(
    uint Pid,
    uint ExitCode,
    DateTimeOffset TimestampUtc)
{
    public bool HasExceptionStatus => (ExitCode & 0xF0000000) == 0xC0000000;
}
