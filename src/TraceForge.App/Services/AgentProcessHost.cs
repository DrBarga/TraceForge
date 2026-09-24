using System.Diagnostics;

namespace TraceForge.App.Services;

public sealed class AgentProcessHost : IDisposable
{
    private readonly object _logSync = new();
    private readonly string _logPath;
    private readonly string _pipeName = $"TraceForge.Agent.v1.{Guid.NewGuid():N}";
    private Process? _process;

    public AgentProcessHost()
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TraceForge",
            "Logs");
        Directory.CreateDirectory(logDirectory);
        _logPath = Path.Combine(logDirectory, "agent.log");
    }

    public string AgentPath => Path.Combine(AppContext.BaseDirectory, "TraceForge.Agent.exe");
    public string PipeName => _pipeName;
    public bool IsRunning => _process is { HasExited: false };

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        DisposeProcess();

        if (!File.Exists(AgentPath))
        {
            throw new FileNotFoundException("TraceForge.Agent.exe was not found in the application directory.", AgentPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = AgentPath,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        startInfo.ArgumentList.Add($"--pipe={_pipeName}");
        _process = Process.Start(startInfo);

        if (_process is null)
        {
            throw new InvalidOperationException("TraceForge Agent could not be started.");
        }

        _process.ErrorDataReceived += (_, args) => WriteLog("stderr", args.Data);
        _process.OutputDataReceived += (_, args) => WriteLog("stdout", args.Data);
        _process.BeginErrorReadLine();
        _process.BeginOutputReadLine();
        WriteLog("host", $"Started agent process {_process.Id}.");
    }

    public void Restart()
    {
        Stop();
        Start();
    }

    public void Stop()
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(true);
                _process.WaitForExit(2000);
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            DisposeProcess();
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
                    _process.WaitForExit(2000);
                }
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            DisposeProcess();
        }
    }

    private void DisposeProcess()
    {
        _process?.Dispose();
        _process = null;
    }

    private void WriteLog(string source, string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        try
        {
            lock (_logSync)
            {
                File.AppendAllText(
                    _logPath,
                    $"{DateTimeOffset.UtcNow:O} [{source}] {message}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
