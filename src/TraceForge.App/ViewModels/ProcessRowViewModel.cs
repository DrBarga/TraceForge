using System.ComponentModel;
using System.Runtime.CompilerServices;
using TraceForge.Application.Models;

namespace TraceForge.App.ViewModels;

public sealed class ProcessRowViewModel : INotifyPropertyChanged
{
    public ProcessRowViewModel(ProcessInfo process)
    {
        Process = process;
    }

    public ProcessInfo Process { get; private set; }

    public void Update(ProcessInfo process)
    {
        if (Process == process)
        {
            return;
        }

        Process = process;
        OnPropertyChanged(nameof(Pid));
        OnPropertyChanged(nameof(ParentPid));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Path));
        OnPropertyChanged(nameof(Cpu));
        OnPropertyChanged(nameof(Memory));
        OnPropertyChanged(nameof(Threads));
        OnPropertyChanged(nameof(Access));
    }
    public string Pid => Process.Pid.ToString();
    public string ParentPid => Process.ParentPid.ToString();
    public string Name => Process.Name;
    public string Path => Process.PathAvailable ? Process.Path : "—";
    public string Cpu => Process.CpuAvailable
        ? $"{Process.CpuPercent:F1}%"
        : Process.CpuError == 0 ? "…" : "—";
    public string Memory => Process.MemoryAvailable
        ? FormatBytes(Process.WorkingSetBytes)
        : "—";
    public string Threads => Process.ThreadCount.ToString();
    public string Access => !Process.Accessible
        ? FormatUnavailable(Process.AccessError)
        : Process.PathAvailable && Process.MemoryAvailable && (Process.CpuAvailable || Process.CpuError == 0)
            ? "Available"
            : "Limited";

    private static string FormatUnavailable(uint error)
    {
        return error == 0 ? "Unavailable" : $"Unavailable (Win32 {error})";
    }

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

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
