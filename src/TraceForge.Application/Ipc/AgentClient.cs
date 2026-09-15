using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using TraceForge.Application.Models;

namespace TraceForge.Application.Ipc;

public sealed class AgentClient : IAsyncDisposable
{
    public const string PipeName = "TraceForge.Agent.v1";

    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public bool IsConnected => _pipe?.IsConnected == true;

    public async Task ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (IsConnected)
        {
            return;
        }

        await DisposePipeAsync();

        _pipe = new NamedPipeClientStream(
            ".",
            PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        await _pipe.ConnectAsync(timeoutSource.Token);

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
        using var document = JsonDocument.Parse(json);
        var type = document.RootElement.GetProperty("type").GetString();
        if (!string.Equals(type, "pong", StringComparison.Ordinal))
        {
            throw new InvalidDataException("TraceForge Agent returned an invalid ping response.");
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
                process.Name ?? string.Empty,
                process.Path ?? string.Empty,
                process.CpuPercent,
                process.WorkingSetBytes,
                process.ThreadCount,
                process.Accessible,
                process.AccessError))
            .ToArray();

        return new ProcessSnapshot(timestamp, processes);
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

    private async Task<string> SendAsync(string type, CancellationToken cancellationToken)
    {
        if (_writer is null || _reader is null || !IsConnected)
        {
            throw new InvalidOperationException("TraceForge Agent is not connected.");
        }

        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            var request = JsonSerializer.Serialize(new AgentRequest(type), JsonOptions);
            await _writer.WriteLineAsync(request.AsMemory(), cancellationToken);
            var response = await _reader.ReadLineAsync(cancellationToken);

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
        finally
        {
            _requestGate.Release();
        }
    }

    private async ValueTask DisposePipeAsync()
    {
        if (_writer is not null)
        {
            await _writer.DisposeAsync();
            _writer = null;
        }

        _reader?.Dispose();
        _reader = null;

        if (_pipe is not null)
        {
            await _pipe.DisposeAsync();
            _pipe = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposePipeAsync();
        _requestGate.Dispose();
    }

    private sealed record AgentRequest(string Type);

    private sealed class SnapshotEnvelope
    {
        public string? Type { get; init; }
        public long TimestampUnixMs { get; init; }
        public ProcessDto[] Processes { get; init; } = [];
    }

    private sealed class ProcessDto
    {
        public uint Pid { get; init; }
        public string? Name { get; init; }
        public string? Path { get; init; }
        public double CpuPercent { get; init; }
        public ulong WorkingSetBytes { get; init; }
        public uint ThreadCount { get; init; }
        public bool Accessible { get; init; }
        public uint AccessError { get; init; }
    }
}
