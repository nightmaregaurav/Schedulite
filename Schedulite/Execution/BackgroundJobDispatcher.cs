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
    /// <summary>Gets the keyed lock that prevents overlapping executions of the same job or subject as needed.</summary>
    private readonly KeyedExecutionLock _executionLock = new();
    /// <summary>Gets the dictionary of currently active executions, keyed by execution ID.</summary>
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
        var scope = lease.Job.ConcurrencyScope;
        ExecutionLockKey? lockKey;
        if (scope == ExecutionConcurrencyScope.Unrestricted)
        {
            lockKey = null;
        }
        else if (scope == ExecutionConcurrencyScope.PerJob)
        {
            lockKey = new ExecutionLockKey(request.JobId, null);
        }
        else if (scope == ExecutionConcurrencyScope.PerSubject)
        {
            lockKey = new ExecutionLockKey(request.JobId, request.SubjectId);
        }
        else
        {
            throw new InvalidOperationException($"Background job '{request.JobId}' has an unsupported concurrency scope '{scope}'.");
        }

        using var executionLock = lockKey is null ? null : await _executionLock.AcquireAsync(lockKey.Value, cancellationToken);

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

    private readonly record struct ExecutionLockKey(string JobId, string? SubjectId);

    /// <summary>Provides per-key asynchronous locks and removes idle keys to bound memory use.</summary>
    private sealed class KeyedExecutionLock
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<ExecutionLockKey, LockEntry> _entries = new();

        public async ValueTask<IDisposable> AcquireAsync(ExecutionLockKey key, CancellationToken cancellationToken)
        {
            LockEntry entry;
            lock (_gate)
            {
                if (!_entries.TryGetValue(key, out entry!))
                {
                    entry = new LockEntry();
                    _entries.Add(key, entry);
                }

                entry.References++;
            }

            try
            {
                await entry.Semaphore.WaitAsync(cancellationToken);
                return new Releaser(this, key, entry);
            }
            catch
            {
                ReleaseReference(key, entry);
                throw;
            }
        }

        private void Release(ExecutionLockKey key, LockEntry entry)
        {
            entry.Semaphore.Release();
            ReleaseReference(key, entry);
        }

        private void ReleaseReference(ExecutionLockKey key, LockEntry entry)
        {
            lock (_gate)
            {
                entry.References--;
                if (entry.References == 0)
                {
                    _entries.Remove(key);
                    entry.Semaphore.Dispose();
                }
            }
        }

        private sealed class LockEntry
        {
            public SemaphoreSlim Semaphore { get; } = new(1, 1);
            public int References { get; set; }
        }

        private sealed class Releaser(KeyedExecutionLock owner, ExecutionLockKey key, LockEntry entry) : IDisposable
        {
            private int _released;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _released, 1) == 0)
                {
                    owner.Release(key, entry);
                }
            }
        }
    }
}
