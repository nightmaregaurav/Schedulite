using Microsoft.Extensions.DependencyInjection;
using Schedulite.Abstractions;
using Schedulite.Execution;

namespace Schedulite.Scheduling;

/// <summary>Loads schedules, queues due executions, and responds to manual triggers.</summary>
internal sealed class BackgroundJobScheduler(
    string? instanceId,
    IServiceScopeFactory scopeFactory,
    InMemoryBackgroundJobExecutionQueue executionQueue,
    SchedulerSignal signal,
    TimeProvider timeProvider
) : IBackgroundJobScheduler {
    // If the schedule is too far in the future, the timer provider may not be able to wait that long.
    // So we limit the wait to a week and then re-evaluate the next occurrence.
    private static readonly TimeSpan MaximumTimerDelay = TimeSpan.FromDays(7);

    // If a schedule is far behind, we don't want to walk through all of missed occurrences.
    // Instead, we ask the provider to jump to the first occurrence after now if we have walked too many.
    private const int MaximumMissedOccurrencesToWalk = 100;

    // The execution schedule dictionary is keyed by schedule ID and contains the next occurrence for each active schedule.
    private readonly Dictionary<string, ExecutionSchedule> _schedules = new(StringComparer.Ordinal);

    /// <summary>Consumes queued requests until cancellation and waits for active executions to finish.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
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
                await signal.WaitAsync(cancellationToken);
                await ReloadAsync(cancellationToken);
                continue;
            }

            var delay = nextExecution.Value - now;
            if (delay <= TimeSpan.Zero)
            {
                continue;
            }

            var reloadRequested = await WaitForNextEventAsync(delay, cancellationToken);

            if (reloadRequested)
            {
                await ReloadAsync(cancellationToken);
            }
        }
        // ReSharper disable once FunctionNeverReturns
    }

    /// <summary>Validates the requested job and queues a manual execution.</summary>
    public async Task<Guid> TriggerAsync(string jobId, string? subjectId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        if (subjectId is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
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
        return executionId;
    }

    /// <summary>Signals the scheduler to reload schedules from the configured provider.</summary>
    public Task ReloadSchedulesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        signal.Signal();
        return Task.CompletedTask;
    }

    /// <summary>Reloads configured schedules and calculates their next occurrences.</summary>
    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        var configuredSchedules = await FromScheduleProvider(provider => provider.GetSchedulesAsync(cancellationToken));
        ArgumentNullException.ThrowIfNull(configuredSchedules);

        var now = timeProvider.GetUtcNow();
        var scheduleIds = new HashSet<string>(StringComparer.Ordinal);
        var refreshedSchedules = new Dictionary<string, ExecutionSchedule>(StringComparer.Ordinal);
        foreach (var schedule in configuredSchedules.Where(x => x.Enabled))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(schedule);
            ArgumentException.ThrowIfNullOrWhiteSpace(schedule.ScheduleId);
            ArgumentException.ThrowIfNullOrWhiteSpace(schedule.JobId);
            ArgumentException.ThrowIfNullOrWhiteSpace(schedule.ScheduleRule);
            if (schedule.SubjectId is not null)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(schedule.SubjectId);
            }

            if (!scheduleIds.Add(schedule.ScheduleId))
            {
                throw new InvalidOperationException($"Duplicate background job schedule ID '{schedule.ScheduleId}'.");
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
                continue;
            }

            var runtimeSchedule = new ExecutionSchedule(schedule, nextExecution.Value);
            refreshedSchedules.Add(schedule.ScheduleId, runtimeSchedule);
        }

        _schedules.Clear();
        foreach (var (scheduleId, runtimeSchedule) in refreshedSchedules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _schedules.Add(scheduleId, runtimeSchedule);
        }
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
            }
        }
    }

    /// <summary>Advances a schedule past its current occurrence and skips missed occurrences.</summary>
    /// <returns>True if the schedule has no more occurrences; otherwise, false.</returns>
    private async Task<bool> AdvanceSchedule(ExecutionSchedule executionSchedule, DateTimeOffset now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var nextExecution = await FromScheduleProvider(provider => provider.GetNextExecution(executionSchedule.Schedule.ScheduleRule, executionSchedule.NextExecution));

        if (nextExecution is null)
        {
            return false;
        }

        if (nextExecution <= executionSchedule.NextExecution)
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

            if (nextExecution <= executionSchedule.NextExecution)
            {
                throw new InvalidOperationException($"Schedule calculator produced an occurrence that does not advance schedule '{executionSchedule.Schedule.ScheduleId}'.");
            }

            executionSchedule.NextExecution = nextExecution.Value;
        }

        return true;
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
