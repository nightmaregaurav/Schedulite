using System.Threading.Channels;

namespace Schedulite.Scheduling;

/// <summary>Wakes the scheduler when the runtime state may need to be reloaded.</summary>
internal sealed class SchedulerSignal
{
    // A capacity-one channel coalesces repeated requests and preserves a request
    // raised between scheduler waits until the scheduler consumes it.
    private readonly Channel<bool> _signals = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        }
    );

    /// <summary>Queues one reload signal, coalescing it with any signal not yet consumed.</summary>
    public void Signal()
    {
        _signals.Writer.TryWrite(true);
    }

    /// <summary>Waits for and consumes a reload signal, honoring cancellation.</summary>
    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        _ = await _signals.Reader.ReadAsync(cancellationToken);
    }
}
