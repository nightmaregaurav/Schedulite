using System.Collections.Concurrent;

namespace Schedulite;

internal sealed class BackgroundJobDispatcher
{
    private readonly IBackgroundJobExecutionQueue _queue;
    private readonly BackgroundJobResolver _resolver;
    private readonly SemaphoreSlim _concurrencyLimiter;

    private readonly ConcurrentDictionary<Guid, Task> _activeExecutions = new();

    public BackgroundJobDispatcher(IBackgroundJobExecutionQueue queue, BackgroundJobResolver resolver, int maxConcurrency)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(resolver);

        if (maxConcurrency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency), maxConcurrency, "The maximum concurrency must be greater than zero.");
        }

        _queue = queue;
        _resolver = resolver;
        _concurrencyLimiter = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var request in _queue.ReadAllAsync(cancellationToken))
            {
                await _concurrencyLimiter.WaitAsync(cancellationToken);

                var executionTask = ExecuteAndReleaseAsync(request, cancellationToken);
                _activeExecutions.TryAdd(request.ExecutionId, executionTask);

                _ = executionTask.ContinueWith(
                    _ => _activeExecutions.TryRemove(request.ExecutionId, out _),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default
                );
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        finally
        {
            var executions = _activeExecutions.Values.ToArray();
            await Task.WhenAll(executions);
        }
    }

    private async Task ExecuteAndReleaseAsync(BackgroundJobExecutionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteAsync(request, cancellationToken);
        }
        catch (Exception)
        {
            // Execution failures are isolated from the dispatcher.
            // An execution observer can be added later if desired.
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }

    private async Task ExecuteAsync(BackgroundJobExecutionRequest request, CancellationToken cancellationToken)
    {
        await using var lease = await _resolver.ResolveAsync(request.JobId, cancellationToken);
        var context = new BackgroundJobContext
        {
            ExecutionId = request.ExecutionId,
            JobId = request.JobId,
            SubjectId = request.SubjectId,
            ScheduleId = request.ScheduleId,
            Trigger = request.Trigger
        };
        await lease.Job.ExecuteAsync(context, cancellationToken);
    }
}
