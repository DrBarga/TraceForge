using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TraceForge.Application.Models;

namespace TraceForge.Application.Ipc;

public sealed class AgentClient : IAsyncDisposable
{
    public const string PipeName = "TraceForge.Agent.v1";
    public const int ProtocolVersion = 1;

    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly TimeSpan _requestTimeout;
    private readonly string _pipeName;
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public bool IsConnected => _pipe?.IsConnected == true;

    public AgentClient(TimeSpan? requestTimeout = null, string? pipeName = null)
    {
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(5);
        if (_requestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        }

        _pipeName = pipeName ?? PipeName;
        ArgumentException.ThrowIfNullOrWhiteSpace(_pipeName);
    }

    public async Task ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (IsConnected)
        {
            return;
        }

        await DisposePipeAsync();

        _pipe = new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await _pipe.ConnectAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await DisposePipeAsync();
            throw new TimeoutException($"TraceForge Agent did not accept a connection within {timeout.TotalSeconds:F1} seconds.");
        }

        _reader = new StreamReader(
            _pipe,
            new UTF8Encoding(false),
            false,
            4096,
            true);

        _writer = new StreamWriter(
            _pipe,
            new UTF8Encoding(false),
            4096,
            true)
        {
            AutoFlush = true,
            NewLine = "\n"
        };
    }

    public async Task PingAsync(CancellationToken cancellationToken = default)
    {
        var json = await SendAsync("ping", cancellationToken);
        var response = JsonSerializer.Deserialize<PongEnvelope>(json, JsonOptions)
            ?? throw new InvalidDataException("TraceForge Agent returned an empty ping response.");

        if (!string.Equals(response.Type, "pong", StringComparison.Ordinal))
        {
            throw new InvalidDataException("TraceForge Agent returned an invalid ping response.");
        }

        if (response.ProtocolVersion != ProtocolVersion)
        {
            throw new InvalidDataException(
                $"TraceForge protocol mismatch. App expects {ProtocolVersion}, Agent reported {response.ProtocolVersion}.");
        }
    }

    public async Task<ProcessSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var json = await SendAsync("snapshot", cancellationToken);
        var envelope = JsonSerializer.Deserialize<SnapshotEnvelope>(json, JsonOptions)
            ?? throw new InvalidDataException("TraceForge Agent returned an empty snapshot response.");

        if (!string.Equals(envelope.Type, "snapshot", StringComparison.Ordinal))
        {
            throw new InvalidDataException("TraceForge Agent returned an unexpected response type.");
        }

        var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(envelope.TimestampUnixMs);
        var processes = envelope.Processes
            .Select(process => new ProcessInfo(
                process.Pid,
                process.ParentPid,
                process.Name ?? string.Empty,
                process.Path ?? string.Empty,
                process.CpuPercent,
                process.WorkingSetBytes,
                process.ThreadCount,
                process.Accessible,
                process.AccessError,
                process.PathAvailable,
                process.PathError,
                process.CpuAvailable,
                process.CpuError,
                process.MemoryAvailable,
                process.MemoryError,
                process.StartTimeUnixMs))
            .ToArray();

        var processExit = envelope.ProcessExit is null
            ? null
            : new ProcessExit(
                envelope.ProcessExit.Pid,
                envelope.ProcessExit.ExitCode,
                DateTimeOffset.FromUnixTimeMilliseconds(envelope.ProcessExit.TimestampUnixMs));

        return new ProcessSnapshot(timestamp, processes, processExit);
    }

    public async Task WatchProcessAsync(uint processId, CancellationToken cancellationToken = default)
    {
        if (processId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId));
        }

        var json = await SendAsync("watch", cancellationToken, processId);
        var response = JsonSerializer.Deserialize<WatchEnvelope>(json, JsonOptions)
            ?? throw new InvalidDataException("TraceForge Agent returned an empty watch response.");

        if (!string.Equals(response.Type, "watch", StringComparison.Ordinal) || response.Pid != processId)
        {
            throw new InvalidDataException("TraceForge Agent returned an invalid watch response.");
        }
    }

    public async Task StopWatchingAsync(CancellationToken cancellationToken = default)
    {
        var json = await SendAsync("unwatch", cancellationToken);
        var response = JsonSerializer.Deserialize<WatchEnvelope>(json, JsonOptions);
        if (!string.Equals(response?.Type, "unwatch", StringComparison.Ordinal))
        {
            throw new InvalidDataException("TraceForge Agent returned an invalid unwatch response.");
        }
    }

    public async Task<ProcessInspection> InspectProcessAsync(
        uint processId,
        CancellationToken cancellationToken = default)
    {
        if (processId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId));
        }

        var json = await SendAsync("inspect", cancellationToken, processId, TimeSpan.FromSeconds(15));
        var response = JsonSerializer.Deserialize<InspectionEnvelope>(json, JsonOptions)
            ?? throw new InvalidDataException("TraceForge Agent returned an empty inspection response.");

        if (!string.Equals(response.Type, "inspection", StringComparison.Ordinal) || response.Pid != processId)
        {
            throw new InvalidDataException("TraceForge Agent returned an invalid inspection response.");
        }

        return new ProcessInspection(
            processId,
            response.StartTimeUnixMs,
            response.Threads,
            response.Modules,
            response.Connections,
            response.ThreadError,
            response.ModuleError,
            response.NetworkError,
            response.IdentityError);
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            return;
        }

        try
        {
            await SendAsync("shutdown", cancellationToken);
        }
        catch (IOException)
        {
        }
    }

    public async Task<string> CaptureDumpAsync(uint processId, CancellationToken cancellationToken = default)
    {
        if (processId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId));
        }

        var json = await SendAsync("captureDump", cancellationToken, processId, TimeSpan.FromMinutes(2));
        var response = JsonSerializer.Deserialize<DumpEnvelope>(json, JsonOptions)
            ?? throw new InvalidDataException("TraceForge Agent returned an empty dump response.");

        if (!string.Equals(response.Type, "dump", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(response.Path))
        {
            throw new InvalidDataException("TraceForge Agent returned an invalid dump response.");
        }

        return response.Path;
    }

    private async Task<string> SendAsync(
        string type,
        CancellationToken cancellationToken,
        uint? processId = null,
        TimeSpan? requestTimeout = null)
    {
        if (_writer is null || _reader is null || !IsConnected)
        {
            throw new InvalidOperationException("TraceForge Agent is not connected.");
        }

        var gateAcquired = false;
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var timeout = requestTimeout ?? _requestTimeout;
        timeoutSource.CancelAfter(timeout);

        try
        {
            await _requestGate.WaitAsync(timeoutSource.Token);
            gateAcquired = true;

            if (_writer is null || _reader is null || !IsConnected)
            {
                throw new InvalidOperationException("TraceForge Agent is not connected.");
            }

            var writer = _writer;
            var reader = _reader;

            var request = JsonSerializer.Serialize(new AgentRequest(type, processId), JsonOptions);
            await writer.WriteLineAsync(request.AsMemory(), timeoutSource.Token);
            var response = await reader.ReadLineAsync(timeoutSource.Token);

            if (response is null)
            {
                throw new EndOfStreamException("TraceForge Agent closed the pipe.");
            }

            using var document = JsonDocument.Parse(response);
            if (document.RootElement.TryGetProperty("type", out var responseType) &&
                string.Equals(responseType.GetString(), "error", StringComparison.Ordinal))
            {
                var message = document.RootElement.TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString()
                    : "Unknown TraceForge Agent error.";
                throw new InvalidOperationException(message);
            }

            return response;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await DisposePipeAsync();
            throw new TimeoutException(
                $"TraceForge Agent did not answer the '{type}' request within {timeout.TotalSeconds:F1} seconds.");
        }
        catch (IOException)
        {
            await DisposePipeAsync();
            throw;
        }
        finally
        {
            if (gateAcquired)
            {
                _requestGate.Release();
            }
        }
    }

    public ValueTask DisconnectAsync()
    {
        return DisposePipeAsync();
    }

    private async ValueTask DisposePipeAsync()
    {
        var writer = _writer;
        var reader = _reader;
        var pipe = _pipe;
        _writer = null;
        _reader = null;
        _pipe = null;

        try
        {
            if (writer is not null)
            {
                await writer.DisposeAsync();
            }
        }
        catch (IOException)
        {
            // A broken pipe cannot be flushed during StreamWriter disposal.
        }
        finally
        {
            reader?.Dispose();
            if (pipe is not null)
            {
                await pipe.DisposeAsync();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposePipeAsync();
        _requestGate.Dispose();
    }

    private sealed record AgentRequest(string Type, uint? Pid = null);

    private sealed class PongEnvelope
    {
        public string? Type { get; init; }
        public int ProtocolVersion { get; init; }
    }

    private sealed class SnapshotEnvelope
    {
        public string? Type { get; init; }
        public long TimestampUnixMs { get; init; }
        public ProcessDto[] Processes { get; init; } = [];
        public ProcessExitDto? ProcessExit { get; init; }
    }

    private sealed class ProcessExitDto
    {
        public uint Pid { get; init; }
        public uint ExitCode { get; init; }
        public long TimestampUnixMs { get; init; }
    }

    private sealed class WatchEnvelope
    {
        public string? Type { get; init; }
        public uint Pid { get; init; }
    }

    private sealed class InspectionEnvelope
    {
        public string? Type { get; init; }
        public uint Pid { get; init; }
        public long StartTimeUnixMs { get; init; }
        public uint IdentityError { get; init; }
        public uint ThreadError { get; init; }
        public uint ModuleError { get; init; }
        public uint NetworkError { get; init; }
        public ThreadDetails[] Threads { get; init; } = [];
        public ModuleDetails[] Modules { get; init; } = [];
        public ConnectionDetails[] Connections { get; init; } = [];
    }

    private sealed class DumpEnvelope
    {
        public string? Type { get; init; }
        public string? Path { get; init; }
    }

    private sealed class ProcessDto
    {
        public uint Pid { get; init; }
        public uint ParentPid { get; init; }
        public long StartTimeUnixMs { get; init; }
        public string? Name { get; init; }
        public string? Path { get; init; }
        public double CpuPercent { get; init; }
        public ulong WorkingSetBytes { get; init; }
        public uint ThreadCount { get; init; }
        public bool Accessible { get; init; }
        public uint AccessError { get; init; }
        public bool PathAvailable { get; init; }
        public uint PathError { get; init; }
        public bool CpuAvailable { get; init; }
        public uint CpuError { get; init; }
        public bool MemoryAvailable { get; init; }
        public uint MemoryError { get; init; }
    }
}
