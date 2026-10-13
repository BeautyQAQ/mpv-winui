using System.Runtime.CompilerServices;
using System.Threading.Channels;
using MpvShell.Player.Abstractions.Events;

namespace MpvShell.Player.LibMpv;

/// <summary>写入与重置由后端状态锁串行化；单一消费者异步读取。</summary>
internal sealed class PlayerEventBuffer
{
    private readonly Channel<QueuedEvent> _events = Channel.CreateBounded<QueuedEvent>(new BoundedChannelOptions(256)
    {
        SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest, AllowSynchronousContinuations = false,
    });
    private EventSnapshot _snapshot = new();
    private long _sequence;
    private bool _isBuffering;

    internal void ResetMedia() => _snapshot = new();
    internal void ClearEnd() => _snapshot = _snapshot with { End = null };
    internal void Complete() => _events.Writer.TryComplete();

    internal void Publish(PlayerEvent playerEvent)
    {
        if (playerEvent is BufferingChanged buffering) _isBuffering = buffering.IsBuffering;
        var current = new StampedEvent(++_sequence, playerEvent, _isBuffering);
        _snapshot = playerEvent switch
        {
            PlaybackStateChanged => _snapshot with { Playback = current },
            TracksChanged => _snapshot with { Tracks = current },
            MediaInfoChanged => _snapshot with { Info = current },
            BackendFaulted => _snapshot with { Fault = current },
            EndReached => _snapshot with { End = current },
            _ => _snapshot,
        };
        _events.Writer.TryWrite(new(current, _snapshot));
    }

    internal async IAsyncEnumerable<PlayerEvent> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        long lastSequence = 0;
        var isBuffering = false;
        await foreach (var queued in _events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            // 只在序号缺口处补偿被挤出的最新状态。每项均来自该存活事件发布时的快照，
            // 不读取写入方当前值，也不重放已送达的错误或 EOF。
            foreach (var item in EventsToDeliver(queued, lastSequence))
            {
                if (item.IsBuffering != isBuffering)
                {
                    isBuffering = item.IsBuffering;
                    yield return new BufferingChanged(isBuffering);
                }
                if (item.Event is not BufferingChanged) yield return item.Event;
            }
            lastSequence = queued.Current.Sequence;
        }
    }

    private static IEnumerable<StampedEvent> EventsToDeliver(QueuedEvent queued, long lastSequence)
    {
        if (queued.Current.Sequence > lastSequence + 1)
        {
            var snapshot = queued.Snapshot;
            StampedEvent?[] latest = [snapshot.Playback, snapshot.Tracks, snapshot.Info, snapshot.Fault, snapshot.End];
            foreach (var item in latest.OfType<StampedEvent>()
                .Where(item => item.Sequence > lastSequence && item.Sequence < queued.Current.Sequence)
                .OrderBy(item => item.Sequence))
                yield return item;
        }
        yield return queued.Current;
    }

    private sealed record EventSnapshot(StampedEvent? Playback = null, StampedEvent? Tracks = null,
        StampedEvent? Info = null, StampedEvent? Fault = null, StampedEvent? End = null);
    private readonly record struct StampedEvent(long Sequence, PlayerEvent Event, bool IsBuffering);
    private readonly record struct QueuedEvent(StampedEvent Current, EventSnapshot Snapshot);
}
