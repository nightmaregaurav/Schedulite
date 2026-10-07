namespace Schedulite;

internal sealed class SchedulerSignal
{
    private readonly Lock _syncRoot = new();
    private TaskCompletionSource<bool> _signal = CreateSignal();

    public void Signal()
    {
        lock (_syncRoot)
        {
            _signal.TrySetResult(true);
        }
    }

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

    private static TaskCompletionSource<bool> CreateSignal()
    {
        return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
