namespace Schedulite.Scheduling;

/// <summary>Wakes the scheduler when runtime state may need to be reloaded.</summary>
internal sealed class SchedulerSignal
{
    private readonly Lock _syncRoot = new();
    private TaskCompletionSource<bool> _signal = CreateSignal();

    /// <summary>Completes the current signal to wake a waiting scheduler.</summary>
    public void Signal()
    {
        lock (_syncRoot)
        {
            _signal.TrySetResult(true);
        }
    }

    /// <summary>Waits for a signal, honoring cancellation, then prepares the next signal.</summary>
    public Task WaitAsync(CancellationToken cancellationToken)
    {
        lock (_syncRoot)
        {
            if (_signal.Task.IsCompleted)
            {
                _signal = CreateSignal();
            }
            return _signal.Task.WaitAsync(cancellationToken);
        }
    }

    /// <summary>Creates a signal task source that runs continuations asynchronously.</summary>
    private static TaskCompletionSource<bool> CreateSignal()
    {
        return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
