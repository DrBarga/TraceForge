using TraceForge.Application.Models;

namespace TraceForge.Application.Diagnostics;

public sealed class BlackBoxBuffer
{
    private readonly TimeSpan _window;
    private readonly LinkedList<ProcessSnapshot> _snapshots = new();
    private readonly object _sync = new();

    public BlackBoxBuffer(TimeSpan window)
    {
        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window));
        }

        _window = window;
    }

    public void Add(ProcessSnapshot snapshot)
    {
        lock (_sync)
        {
            _snapshots.AddLast(snapshot);
            var cutoff = snapshot.TimestampUtc - _window;

            while (_snapshots.First is not null && _snapshots.First.Value.TimestampUtc < cutoff)
            {
                _snapshots.RemoveFirst();
            }
        }
    }

    public IReadOnlyList<ProcessSnapshot> Snapshot()
    {
        lock (_sync)
        {
            return _snapshots.ToArray();
        }
    }
}
