using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Schedulite.Abstractions;
using Schedulite.Configuration;

namespace Schedulite.Execution;

/// <summary>Dispatches queued job requests while limiting concurrent executions.</summary>
internal sealed class BackgroundJobDispatcher
{
    // The schedulite instance Id, used to resolve keyed services
    private readonly string? _instanceId;
    // The service scope factory, used to create a new scope for each job execution
    private readonly IServiceScopeFactory _scopeFactory;
    // The queue of job execution requests to read requests from. It's populated by the BackgroundJobScheduler.
    private readonly InMemoryBackgroundJobExecutionQueue _queue;
    // The logger, used to log errors and information about job executions
    private readonly ILogger<BackgroundJobDispatcher> _logger;
    // The semaphore that limits the number of concurrent executions per schedulite instance. It's initialized with the maximum concurrency from the options.
    private readonly SemaphoreSlim _concurrencyLimiter;
    private readonly int _maxConcurrency;
    // The keyed execution lock, used to limit concurrent executions of the same job or subject based on the job's concurrency rule
    private readonly KeyedExecutionLock _executionLock = new();
    // The dictionary of active executions, used to track the tasks of currently executing jobs and wait for them to finish on shutdown
    private readonly ConcurrentDictionary<Guid, Task> _activeExecutions = new();

    public BackgroundJobDispatcher(
        string? instanceId,
        ScheduliteOptions options,
        IServiceScopeFactory scopeFactory,
        InMemoryBackgroundJobExecutionQueue queue,
        ILogger<BackgroundJobDispatcher> logger
    ) {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(logger);

        if (options.MaxConcurrency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.MaxConcurrency), options.MaxConcurrency, "The maximum concurrency must be greater than zero.");
        }

        _instanceId = instanceId;
        _scopeFactory = scopeFactory;
        _queue = queue;
        _logger = logger;
        _maxConcurrency = options.MaxConcurrency;
        _concurrencyLimiter = new SemaphoreSlim(options.MaxConcurrency, options.MaxConcurrency);
    }

    /// <summary>Consumes queued requests until cancellation and waits for active executions to finish.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Starting job dispatcher for Schedulite runtime {InstanceId} with a concurrency limit of {MaxConcurrency}.",
            _instanceId,
            _maxConcurrency
        );
        try
        {
            await foreach (var request in _queue.ReadAllAsync(cancellationToken))
            {
                // A slot is held for the whole request, including time spent waiting for a per-job lock.
                _logger.LogTrace("Waiting for a dispatcher slot for execution {ExecutionId} in Schedulite runtime {InstanceId}.", request.ExecutionId, _instanceId);
                await _concurrencyLimiter.WaitAsync(cancellationToken);
                _logger.LogTrace("Acquired a dispatcher slot for execution {ExecutionId} in Schedulite runtime {InstanceId}.", request.ExecutionId, _instanceId);

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
            // Make sure all active executions are completed gracefully
            var executions = _activeExecutions.Values.ToArray();
            _logger.LogInformation(
                "Stopping job dispatcher for Schedulite runtime {InstanceId}; waiting for {ActiveExecutionCount} active executions.",
                _instanceId,
                executions.Length
            );
            await Task.WhenAll(executions);
            _logger.LogInformation("Stopped job dispatcher for Schedulite runtime {InstanceId}.", _instanceId);
        }
    }

    /// <summary>Runs one request and releases its concurrency slot even when execution fails.</summary>
    private async Task ExecuteAndReleaseAsync(BackgroundJobExecutionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(
                "Canceled execution {ExecutionId} for job {JobId} during Schedulite runtime {InstanceId} shutdown.",
                request.ExecutionId,
                request.JobId,
                _instanceId
            );
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Execution {ExecutionId} of job {JobId} for subject {SubjectId} and schedule {ScheduleId} ({Trigger}) in Schedulite runtime {InstanceId} failed.",
                request.ExecutionId,
                request.JobId,
                request.SubjectId,
                request.ScheduleId,
                request.Trigger,
                _instanceId
            );
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }

    /// <summary>Resolves a job, applies its concurrency rule, and executes it in a dependency-injection scope.</summary>
    private async Task ExecuteAsync(BackgroundJobExecutionRequest request, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var job = scope.ServiceProvider.GetKeyedService<IBackgroundJob>((_instanceId, request.JobId));
        if (job == null)
        {
            throw new InvalidOperationException($"Job '{request.JobId}' is not registered for {nameof(Schedulite)} instance '{_instanceId}'");
        }

        var lockKey = job.ConcurrencyScope switch
        {
            ExecutionConcurrencyScope.Unrestricted => (ExecutionLockKey?)null,
            ExecutionConcurrencyScope.PerJob => new ExecutionLockKey(request.JobId, null),
            ExecutionConcurrencyScope.PerSubject => new ExecutionLockKey(request.JobId, request.SubjectId),
            _ => throw new InvalidOperationException($"Background job '{request.JobId}' has an unsupported concurrency scope '{job.ConcurrencyScope}'.")
        };

        var lockWaitStart = lockKey is null ? 0L : Stopwatch.GetTimestamp();
        using var executionLock = lockKey is null ? null : await _executionLock.AcquireAsync(lockKey.Value, cancellationToken);
        if (lockKey is not null)
        {
            var lockWait = Stopwatch.GetElapsedTime(lockWaitStart);
            if (lockWait >= TimeSpan.FromSeconds(1))
            {
                _logger.LogWarning(
                    "Execution {ExecutionId} waited {LockWait} for the {ConcurrencyScope} concurrency lock for job {JobId} and subject {SubjectId} in Schedulite runtime {InstanceId}.",
                    request.ExecutionId,
                    lockWait,
                    job.ConcurrencyScope,
                    request.JobId,
                    request.SubjectId,
                    _instanceId
                );
            }
            else if (lockWait >= TimeSpan.FromMilliseconds(100))
            {
                _logger.LogDebug(
                    "Execution {ExecutionId} waited {LockWait} for the {ConcurrencyScope} concurrency lock for job {JobId} and subject {SubjectId} in Schedulite runtime {InstanceId}.",
                    request.ExecutionId,
                    lockWait,
                    job.ConcurrencyScope,
                    request.JobId,
                    request.SubjectId,
                    _instanceId
                );
            }
        }

        var context = new BackgroundJobContext
        {
            ExecutionId = request.ExecutionId,
            InstanceId = _instanceId,
            JobId = request.JobId,
            SubjectId = request.SubjectId,
            ScheduleId = request.ScheduleId,
            Trigger = request.Trigger
        };
        var executionStart = Stopwatch.GetTimestamp();
        _logger.LogInformation(
            "Starting {Trigger} execution {ExecutionId} of job {JobId} for subject {SubjectId} and schedule {ScheduleId} in Schedulite runtime {InstanceId}.",
            request.Trigger,
            request.ExecutionId,
            request.JobId,
            request.SubjectId,
            request.ScheduleId,
            _instanceId
        );
        await job.ExecuteAsync(context, cancellationToken);
        _logger.LogInformation(
            "Completed execution {ExecutionId} of job {JobId} in {Duration} in Schedulite runtime {InstanceId}.",
            request.ExecutionId,
            request.JobId,
            Stopwatch.GetElapsedTime(executionStart),
            _instanceId
        );
    }
}
