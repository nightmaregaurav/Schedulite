using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Schedulite.Abstractions;
using Schedulite.Execution;

namespace Schedulite.Scheduling;

/// <summary>Loads schedules, queues due executions, and responds to manual triggers.</summary>
internal sealed class BackgroundJobScheduler(
    string? instanceId,
    IServiceScopeFactory scopeFactory,
    InMemoryBackgroundJobExecutionQueue executionQueue,
    SchedulerSignal signal,
    TimeProvider timeProvider,
    BackgroundJobRegistry jobRegistry,
    ILogger<BackgroundJobScheduler> logger
) : IBackgroundJobScheduler {
    // If the schedule is too far in the future, the timer provider may not be able to wait that long.
    // So we limit the wait to a week and then re-evaluate the next occurrence.
    private static readonly TimeSpan MaximumTimerDelay = TimeSpan.FromDays(7);

    // If a schedule is far behind, we don't want to walk through all of missed occurrences.
    // Instead, we ask the provider to jump to the first occurrence after now if we have walked too many.
    private const int MaximumMissedOccurrencesToWalk = 1_000;

    // The execution schedule dictionary is keyed by schedule ID and contains the next occurrence for each active schedule.
    private readonly Dictionary<string, ExecutionSchedule> _schedules = new(StringComparer.Ordinal);
    // A concurrent snapshot lets management APIs read each schedule's current queued occurrence safely.
    private readonly ConcurrentDictionary<string, DateTimeOffset> _queuedScheduleExecutions = new(StringComparer.Ordinal);

    /// <summary>Looks up the next occurrence currently held by the scheduler for a schedule ID.</summary>
    public bool TryGetNextExecution(string scheduleId, out DateTimeOffset nextExecution)
    {
        return _queuedScheduleExecutions.TryGetValue(scheduleId, out nextExecution);
    }

    /// <summary>Consumes queued requests until cancellation and waits for active executions to finish.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting schedule polling for Schedulite runtime {InstanceId}.", instanceId);
        try
        {
            await ReloadAsync(cancellationToken);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var now = timeProvider.GetUtcNow();
                await ExecuteDueSchedulesAsync(now, cancellationToken);

                var nextExecution = GetNextExecution(cancellationToken);

                if (nextExecution is null)
                {
                    logger.LogTrace("No scheduled executions are pending in Schedulite runtime {InstanceId}; waiting for a reload request.", instanceId);
                    await signal.WaitAsync(cancellationToken);
                    await ReloadAsync(cancellationToken);
                    continue;
                }

                var delay = nextExecution.Value - now;
                if (delay <= TimeSpan.Zero)
                {
                    logger.LogTrace("Schedule polling will immediately recheck due executions in Schedulite runtime {InstanceId}.", instanceId);
                    continue;
                }

                logger.LogTrace(
                    "Next scheduled execution in Schedulite runtime {InstanceId} is at {NextExecutionUtc}; waiting for {Delay} or a reload request.",
                    instanceId,
                    nextExecution,
                    delay
                );
                var reloadRequested = await WaitForNextEventAsync(delay, cancellationToken);

                if (reloadRequested)
                {
                    await ReloadAsync(cancellationToken);
                }
            }
        }
        finally
        {
            logger.LogInformation("Stopped schedule polling for Schedulite runtime {InstanceId}.", instanceId);
        }
    }

    /// <summary>Validates the requested job and queues a manual execution.</summary>
    public async Task<Guid> TriggerAsync(string jobId, string? subjectId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        if (subjectId is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        }

        if (!jobRegistry.Contains(jobId))
        {
            logger.LogWarning("Rejected a manual trigger for unregistered job {JobId} in Schedulite runtime {InstanceId}.", jobId, instanceId);
            throw new KeyNotFoundException($"Background job '{jobId}' is not registered for Schedulite runtime '{instanceId ?? "<default>"}'.");
        }

        var executionId = Guid.NewGuid();
        var request = new BackgroundJobExecutionRequest(
            executionId,
            jobId,
            subjectId,
            null,
            BackgroundJobTrigger.Manual
        );

        await executionQueue.EnqueueAsync(request, cancellationToken);
        logger.LogInformation(
            "Accepted manual execution {ExecutionId} for job {JobId} and subject {SubjectId} in Schedulite runtime {InstanceId}.",
            executionId,
            jobId,
            subjectId,
            instanceId
        );
        return executionId;
    }

    /// <summary>Signals the scheduler to reload schedules from the configured provider.</summary>
    public Task ReloadSchedulesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        signal.Signal();
        logger.LogInformation("Requested a schedule reload for Schedulite runtime {InstanceId}.", instanceId);
        return Task.CompletedTask;
    }

    /// <summary>Reloads configured schedules and calculates their next occurrences.</summary>
    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Loading configured schedules for Schedulite runtime {InstanceId}.", instanceId);
        var configuredSchedules = await FromScheduleProvider(provider => provider.GetSchedulesAsync(cancellationToken));
        ArgumentNullException.ThrowIfNull(configuredSchedules);

        var now = timeProvider.GetUtcNow();
        var scheduleIds = new HashSet<string>(StringComparer.Ordinal);
        var refreshedSchedules = new Dictionary<string, ExecutionSchedule>(StringComparer.Ordinal);
        var disabledScheduleCount = 0;
        var schedulesWithoutNextOccurrence = 0;
        foreach (var schedule in configuredSchedules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(schedule);
            ArgumentException.ThrowIfNullOrWhiteSpace(schedule.ScheduleId);
            if (!scheduleIds.Add(schedule.ScheduleId))
            {
                throw new InvalidOperationException($"Duplicate background job schedule ID '{schedule.ScheduleId}'.");
            }

            if (!schedule.Enabled)
            {
                disabledScheduleCount++;
                logger.LogTrace("Ignoring disabled schedule {ScheduleId} in Schedulite runtime {InstanceId}.", schedule.ScheduleId, instanceId);
                continue;
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(schedule.JobId);
            ArgumentException.ThrowIfNullOrWhiteSpace(schedule.ScheduleRule);
            if (schedule.SubjectId is not null)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(schedule.SubjectId);
            }

            DateTimeOffset? nextExecution;
            if (_schedules.TryGetValue(schedule.ScheduleId, out var existingSchedule) && StringComparer.Ordinal.Equals(existingSchedule.Schedule.ScheduleRule, schedule.ScheduleRule))
            {
                nextExecution = existingSchedule.NextExecution;
            }
            else
            {
                nextExecution = await FromScheduleProvider(provider => provider.GetNextExecution(schedule.ScheduleRule, now));
            }
            if (nextExecution is null)
            {
                schedulesWithoutNextOccurrence++;
                logger.LogDebug(
                    "Schedule {ScheduleId} has no future occurrence and will not be queued in Schedulite runtime {InstanceId}.",
                    schedule.ScheduleId,
                    instanceId
                );
                continue;
            }

            var runtimeSchedule = new ExecutionSchedule(schedule, nextExecution.Value);
            refreshedSchedules.Add(schedule.ScheduleId, runtimeSchedule);
            logger.LogTrace(
                "Loaded schedule {ScheduleId} for job {JobId}; its next execution is {NextExecutionUtc} in Schedulite runtime {InstanceId}.",
                schedule.ScheduleId,
                schedule.JobId,
                nextExecution,
                instanceId
            );
        }

        _schedules.Clear();
        _queuedScheduleExecutions.Clear();
        foreach (var (scheduleId, runtimeSchedule) in refreshedSchedules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _schedules.Add(scheduleId, runtimeSchedule);
            _queuedScheduleExecutions[scheduleId] = runtimeSchedule.NextExecution;
        }

        logger.LogInformation(
            "Reloaded schedules for Schedulite runtime {InstanceId}: {ScheduledScheduleCount} scheduled, {DisabledScheduleCount} disabled, {WithoutNextOccurrenceCount} enabled without a future occurrence.",
            instanceId,
            refreshedSchedules.Count,
            disabledScheduleCount,
            schedulesWithoutNextOccurrence
        );
    }

    /// <summary>Queues each due schedule and advances it to its next occurrence.</summary>
    private async Task ExecuteDueSchedulesAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Record exhausted entries and remove them after enumeration; mutating the dictionary
        // inside the loop would invalidate its enumerator.
        List<string>? exhaustedScheduleIds = null;
        foreach (var runtimeSchedule in _schedules.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (runtimeSchedule.NextExecution > now)
            {
                continue;
            }

            var request = new BackgroundJobExecutionRequest(
                Guid.NewGuid(),
                runtimeSchedule.Schedule.JobId,
                runtimeSchedule.Schedule.SubjectId,
                runtimeSchedule.Schedule.ScheduleId,
                BackgroundJobTrigger.Scheduled
            );

            await executionQueue.EnqueueAsync(request, cancellationToken);
            if (await AdvanceSchedule(runtimeSchedule, now, cancellationToken))
            {
                _queuedScheduleExecutions[runtimeSchedule.Schedule.ScheduleId] = runtimeSchedule.NextExecution;
                continue;
            }

            exhaustedScheduleIds ??= [];
            exhaustedScheduleIds.Add(runtimeSchedule.Schedule.ScheduleId);
        }

        if (exhaustedScheduleIds is not null)
        {
            foreach (var scheduleId in exhaustedScheduleIds)
            {
                _schedules.Remove(scheduleId);
                _queuedScheduleExecutions.TryRemove(scheduleId, out _);
            }
        }
    }

    /// <summary>Advances a schedule past its current occurrence and skips missed occurrences.</summary>
    /// <returns>True if the schedule has another occurrence; otherwise, false.</returns>
    private async Task<bool> AdvanceSchedule(ExecutionSchedule executionSchedule, DateTimeOffset now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var nextExecution = await FromScheduleProvider(provider => provider.GetNextExecution(executionSchedule.Schedule.ScheduleRule, executionSchedule.NextExecution));

        if (nextExecution is null)
        {
            return false;
        }

        if (nextExecution == executionSchedule.NextExecution)
        {
            LogOneTimeSchedule(executionSchedule, nextExecution.Value);
            return false;
        }

        if (nextExecution < executionSchedule.NextExecution)
        {
            throw new InvalidOperationException($"Schedule calculator produced an occurrence that does not advance schedule '{executionSchedule.Schedule.ScheduleId}'.");
        }

        executionSchedule.NextExecution = nextExecution.Value;

        var missedOccurrencesWalked = 0;
        while (executionSchedule.NextExecution <= now)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (missedOccurrencesWalked >= MaximumMissedOccurrencesToWalk)
            {
                // A long outage can leave a high-frequency schedule millions of occurrences behind.
                // Ask the provider to jump to the first occurrence after now instead of walking all of them.
                logger.LogWarning(
                    "Schedule {ScheduleId} in Schedulite runtime {InstanceId} has more than {MaximumMissedOccurrences} missed occurrences; asking the provider to fast-forward.",
                    executionSchedule.Schedule.ScheduleId,
                    instanceId,
                    MaximumMissedOccurrencesToWalk
                );
                nextExecution = await FromScheduleProvider(provider => provider.GetNextExecution(executionSchedule.Schedule.ScheduleRule, now));
                if (nextExecution is null)
                {
                    return false;
                }

                if (nextExecution <= now)
                {
                    throw new InvalidOperationException($"Schedule calculator produced an occurrence that does not advance past the current time for schedule '{executionSchedule.Schedule.ScheduleId}'.");
                }

                executionSchedule.NextExecution = nextExecution.Value;
                return true;
            }

            missedOccurrencesWalked++;
            nextExecution = await FromScheduleProvider(provider => provider.GetNextExecution(executionSchedule.Schedule.ScheduleRule, executionSchedule.NextExecution));
            if (nextExecution is null)
            {
                return false;
            }

            if (nextExecution == executionSchedule.NextExecution)
            {
                LogOneTimeSchedule(executionSchedule, nextExecution.Value);
                return false;
            }

            if (nextExecution < executionSchedule.NextExecution)
            {
                throw new InvalidOperationException($"Schedule calculator produced an occurrence that does not advance schedule '{executionSchedule.Schedule.ScheduleId}'.");
            }

            executionSchedule.NextExecution = nextExecution.Value;
        }

        return true;
    }

    /// <summary>Logs that a schedule provider returned its current occurrence again, marking the schedule as one-time.</summary>
    private void LogOneTimeSchedule(ExecutionSchedule executionSchedule, DateTimeOffset executionTime)
    {
        logger.LogInformation(
            "Schedule {ScheduleId} returned its current execution time {ExecutionTimeUtc}; treating it as a one-time schedule and removing it from the schedule queue in Schedulite runtime {InstanceId}.",
            executionSchedule.Schedule.ScheduleId,
            executionTime,
            instanceId
        );
    }

    /// <summary>Returns the earliest next occurrence among active schedules.</summary>
    private DateTimeOffset? GetNextExecution(CancellationToken cancellationToken)
    {
        DateTimeOffset? nextExecution = null;
        foreach (var schedule in _schedules.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (nextExecution is null || schedule.NextExecution < nextExecution)
            {
                nextExecution = schedule.NextExecution;
            }
        }
        return nextExecution;
    }

    /// <summary>Waits for the next timer deadline or a scheduler reload signal.</summary>
    private async Task<bool> WaitForNextEventAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delayTask = Task.Delay(delay < MaximumTimerDelay ? delay : MaximumTimerDelay, timeProvider, waitCancellation.Token);
        var signalTask = signal.WaitAsync(waitCancellation.Token);
        var reloadRequested = false;

        try
        {
            var completedTask = await Task.WhenAny(delayTask, signalTask);
            await completedTask;
            reloadRequested = completedTask == signalTask;
            return reloadRequested;
        }
        finally
        {
            // Stop the losing wait so it cannot consume a later reload signal or retain a long timer.
            await waitCancellation.CancelAsync();
            // If a signal won the race with the timer but the timer task was selected,
            // put it back so the next scheduler wait still observes the reload request.
            if (!reloadRequested && signalTask.IsCompletedSuccessfully)
            {
                signal.Signal();
            }
        }
    }

    /// <summary>Resolves the keyed provider in a scope for one call and then disposes that scope.</summary>
    private async Task<TResult> FromScheduleProvider<TResult>(Func<IBackgroundJobScheduleProvider, Task<TResult>> call)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredKeyedService<IBackgroundJobScheduleProvider>(instanceId);
        return await call(provider);
    }
}
