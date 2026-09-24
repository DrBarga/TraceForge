using TraceForge.Application.Diagnostics;
using TraceForge.Application.Models;
using Xunit;

namespace TraceForge.Application.Tests;

public sealed class BlackBoxBufferTests
{
    [Fact]
    public void Add_EvictsSnapshotsOutsideWindow()
    {
        var buffer = new BlackBoxBuffer(TimeSpan.FromSeconds(10));
        var start = new DateTimeOffset(2026, 9, 22, 20, 0, 0, TimeSpan.Zero);

        buffer.Add(new ProcessSnapshot(start, []));
        buffer.Add(new ProcessSnapshot(start.AddSeconds(8), []));
        buffer.Add(new ProcessSnapshot(start.AddSeconds(11), []));

        var snapshots = buffer.Snapshot();

        Assert.Equal(2, snapshots.Count);
        Assert.Equal(start.AddSeconds(8), snapshots[0].TimestampUtc);
        Assert.Equal(start.AddSeconds(11), snapshots[1].TimestampUtc);
    }

    [Fact]
    public void Constructor_RejectsNonPositiveWindow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BlackBoxBuffer(TimeSpan.Zero));
    }
}
