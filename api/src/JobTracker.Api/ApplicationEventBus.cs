using System.Threading.Channels;

namespace JobTracker.Api;

/// <summary>
/// Broadcasts "the catalog changed" to every open event stream (BEHAVIOR-m3-backend-api-046, spec
/// DECISION-m3-backend-api-005).
///
/// <para>
/// Each subscriber gets its <b>own</b> channel and every channel is written to, which is the difference between a
/// broadcast and a work queue: a single <see cref="Channel{T}"/> read by all connections would deliver each id to
/// <i>one</i> arbitrary client, and the visible symptom would be a two-tab app where the tab that did not write is
/// the only one that never updates — the exact property the <c>StorageEvent</c> path has provided since M2, now
/// silently absent.
/// </para>
///
/// <para>
/// <b>Why a dropped event is acceptable and a blocked writer never is.</b> The buffer is bounded and overflows by
/// dropping (<see cref="BoundedChannelFullMode.DropWrite"/>), because the message means "your view may be stale —
/// re-read", not "here is a record to insert". Losing one of those costs a client one redundant re-read's worth of
/// freshness; it never costs data, since the catalog is fetched after it anyway. Letting the buffer fill with
/// <c>Wait</c> instead would put a slow HTTP client — a backgrounded phone tab, a buffering proxy — in the write path
/// of every application, where it would apply back-pressure to <c>SaveChangesAsync</c>'s caller. A reader on the
/// other side of a dead connection cannot be relied upon to unblock it, so the API's availability would be bounded by
/// the worst-connected subscriber. That trade is the whole design.
/// </para>
///
/// <para>
/// State is per-process, so two instances each notify their own connected clients and no others. That is
/// <b>ASSUMPTION-m3-backend-api-002</b> (single instance through M5) in the running code rather than only in a
/// document; the remedy when it is lifted is a Redis backplane or PostgreSQL <c>NOTIFY</c> (DECISION-005's rejected
/// options both name it), and no behaviour here needs to change to accommodate that — only this class.
/// </para>
/// </summary>
public sealed class ApplicationEventBus
{
    /// <summary>
    /// Per-subscriber depth. Sixty un-read ids is roughly sixty seconds of writes at a pace this app will never
    /// reach; past that the subscriber is not slow, it is gone, and the newest events matter more than the oldest.
    /// </summary>
    private const int BufferPerSubscriber = 64;

    private readonly object _gate = new();
    private readonly List<Channel<string>> _subscribers = [];

    /// <summary>
    /// Starts a subscription. The caller must <see cref="Unsubscribe"/> it — the SSE endpoint does so in a
    /// <c>finally</c>, because a channel left behind after a browser vanished is a memory leak with a writer
    /// attached to it.
    /// </summary>
    public Channel<string> Subscribe()
    {
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(BufferPerSubscriber)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
        });

        lock (_gate)
        {
            _subscribers.Add(channel);
        }

        return channel;
    }

    public void Unsubscribe(Channel<string> channel)
    {
        lock (_gate)
        {
            _subscribers.Remove(channel);
        }

        // Completing the writer is what lets the reader's `ReadAllAsync` end cleanly if it is still parked, rather
        // than waiting for a cancellation that may already have been observed by the HTTP layer.
        channel.Writer.TryComplete();
    }

    /// <summary>
    /// Announces one committed write. Never blocks and never throws at a caller that has already saved: the catalog
    /// mutation is the fact, and an undeliverable notification about it is a staleness problem, not a failure.
    /// </summary>
    public void Publish(string applicationId)
    {
        Channel<string>[] snapshot;

        lock (_gate)
        {
            snapshot = [.. _subscribers];
        }

        foreach (var channel in snapshot)
        {
            channel.Writer.TryWrite(applicationId);
        }
    }
}
