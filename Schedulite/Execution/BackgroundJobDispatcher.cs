using System.Collections.Concurrent;
using Schedulite.Abstractions;

namespace Schedulite.Execution;

/// <summary>Dispatches queued job requests while limiting concurrent executions.</summary>
internal sealed class BackgroundJobDispatcher
{
    /// <summary>Gets the queue from which execution requests are read.</summary>
    private readonly IBackgroundJobExecutionQueue _queue;
    /// <summary>Gets the resolver used to create scoped job instances.</summary>
    private readonly BackgroundJobResolver _resolver;
    /// <summary>Gets the semaphore that limits simultaneous job executions.</summary>
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

    /// <summary>Consumes queued requests until cancellation and waits for active executions to finish.</summary>
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
                    completedTask => _activeExecutions.TryRemove(request.ExecutionId, out _),
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

    /// <summary>Runs one request and releases its concurrency slot even when execution fails.</summary>
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

    /// <summary>Coordinates scheduler and dispatcher lifetimes until shutdown.</summary>
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
