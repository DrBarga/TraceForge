using System.Diagnostics;

namespace TraceForge.App.Services;

public sealed class AgentProcessHost : IDisposable
{
    private Process? _process;

    public string AgentPath => Path.Combine(AppContext.BaseDirectory, "TraceForge.Agent.exe");

    public void Start()
    {
        if (_process is { HasExited: false })
        {
            return;
        }

        if (!File.Exists(AgentPath))
        {
            throw new FileNotFoundException("TraceForge.Agent.exe was not found in the application directory.", AgentPath);
        }

        _process = Process.Start(new ProcessStartInfo
        {
            FileName = AgentPath,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        });

        if (_process is null)
        {
            throw new InvalidOperationException("TraceForge Agent could not be started.");
        }
    }

    public void Dispose()
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                if (!_process.WaitForExit(1500))
                {
                    _process.Kill(true);
                }
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }
}
