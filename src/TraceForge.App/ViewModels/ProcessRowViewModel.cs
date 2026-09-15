using TraceForge.Application.Models;

namespace TraceForge.App.ViewModels;

public sealed class ProcessRowViewModel
{
    public ProcessRowViewModel(ProcessInfo process)
    {
        Process = process;
    }

    public ProcessInfo Process { get; }
    public string Pid => Process.Pid.ToString();
    public string Name => Process.Name;
    public string Path => Process.Path;
    public string Cpu => $"{Process.CpuPercent:F1}%";
    public string Memory => FormatBytes(Process.WorkingSetBytes);
    public string Threads => Process.ThreadCount.ToString();
    public string Access => Process.Accessible ? "OK" : $"Win32 {Process.AccessError}";

    private static string FormatBytes(ulong bytes)
    {
        if (bytes >= 1024UL * 1024 * 1024)
        {
            return $"{bytes / 1024d / 1024d / 1024d:F2} GiB";
        }

        if (bytes >= 1024UL * 1024)
        {
            return $"{bytes / 1024d / 1024d:F1} MiB";
        }

        return $"{bytes / 1024d:F0} KiB";
    }
}
