using System.IO.Pipes;
using System.Text;
using TraceForge.Application.Ipc;
using Xunit;

namespace TraceForge.Application.Tests;

public sealed class AgentClientTests
{
    [Fact]
    public async Task PingAsync_TimesOutWhenAgentDoesNotRespond()
    {
        var pipeName = $"TraceForge.Tests.{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        await using var client = new AgentClient(TimeSpan.FromMilliseconds(150), pipeName);

        var accept = server.WaitForConnectionAsync();
        await client.ConnectAsync(TimeSpan.FromSeconds(2));
        await accept;

        await Assert.ThrowsAsync<TimeoutException>(() => client.PingAsync());
    }

    [Fact]
    public async Task PingAsync_RejectsMismatchedProtocol()
    {
        var pipeName = $"TraceForge.Tests.{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        await using var client = new AgentClient(TimeSpan.FromSeconds(2), pipeName);

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            using var reader = new StreamReader(server, new UTF8Encoding(false), leaveOpen: true);
            await using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = "\n"
            };
            await reader.ReadLineAsync();
            await writer.WriteLineAsync("{\"type\":\"pong\",\"protocolVersion\":2}");
        });

        await client.ConnectAsync(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAsync<InvalidDataException>(() => client.PingAsync());
        await serverTask;
    }

    [Fact]
    public async Task PingAsync_CleansUpAfterAgentDisconnects()
    {
        var pipeName = $"TraceForge.Tests.{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        await using var client = new AgentClient(TimeSpan.FromSeconds(2), pipeName);

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            using var reader = new StreamReader(server, new UTF8Encoding(false), leaveOpen: true);
            await reader.ReadLineAsync();
            server.Disconnect();
        });

        await client.ConnectAsync(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAnyAsync<IOException>(() => client.PingAsync());
        await serverTask;
        await client.DisconnectAsync();
        Assert.False(client.IsConnected);
    }
}
