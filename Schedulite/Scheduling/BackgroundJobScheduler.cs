using Schedulite.Abstractions;
using Schedulite.Execution;

namespace Schedulite.Scheduling;

internal sealed class BackgroundJobScheduler(IBackgroundJobScheduleProvider scheduleProvider, IBackgroundJobExecutionQueue executionQueue, BackgroundJobRegistry registry, SchedulerSignal signal, TimeProvider timeProvider) : IBackgroundJobScheduler
{
    private readonly Dictionary<string, RuntimeSchedule> _schedules = new(StringComparer.Ordinal);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await ReloadAsync(cancellationToken);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var now = timeProvider.GetUtcNow();
            await ExecuteDueSchedulesAsync(now, cancellationToken);

            var nextExecution = GetNextExecution();

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

    public async Task<Guid> TriggerAsync(string jobId, string subjectId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);

        if (!registry.Contains(jobId))
        {
            throw new KeyNotFoundException($"No background job with ID '{jobId}' is registered.");
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

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        var configuredSchedules = await scheduleProvider.GetSchedulesAsync(cancellationToken);
        _schedules.Clear();

        var now = timeProvider.GetUtcNow();
        foreach (var schedule in configuredSchedules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentException.ThrowIfNullOrWhiteSpace(schedule.ScheduleId);

            if (!schedule.Enabled)
            {
                continue;
            }

            if (!registry.Contains(schedule.JobId))
            {
                throw new InvalidOperationException($"Schedule '{schedule.ScheduleId}' references unregistered background job '{schedule.JobId}'.");
            }

            var nextExecution = await scheduleProvider.GetNextExecution(schedule.ScheduleRule, now);
            if (nextExecution is null)
            {
                continue;
            }

            var runtimeSchedule = new RuntimeSchedule
            {
                Schedule = schedule,
                NextExecution = nextExecution.Value
            };

            if (!_schedules.TryAdd(schedule.ScheduleId, runtimeSchedule))
            {
                throw new InvalidOperationException($"Duplicate background job schedule ID '{schedule.ScheduleId}'.");
            }
        }
    }

    private async Task ExecuteDueSchedulesAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var runtimeSchedule in _schedules.Values)
        {
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
            await AdvanceSchedule(runtimeSchedule, now);
        }
    }

    private async Task AdvanceSchedule(RuntimeSchedule runtimeSchedule, DateTimeOffset now)
    {
        var nextExecution = await scheduleProvider.GetNextExecution(runtimeSchedule.Schedule.ScheduleRule, runtimeSchedule.NextExecution);

        if (nextExecution is null)
        {
            _schedules.Remove(runtimeSchedule.Schedule.ScheduleId);
            return;
        }

        if (nextExecution <= runtimeSchedule.NextExecution)
        {
            throw new InvalidOperationException($"Schedule calculator produced an occurrence that does not advance schedule '{runtimeSchedule.Schedule.ScheduleId}'.");
        }

        runtimeSchedule.NextExecution = nextExecution.Value;

        while (runtimeSchedule.NextExecution <= now)
        {
            nextExecution = await scheduleProvider.GetNextExecution(runtimeSchedule.Schedule.ScheduleRule, runtimeSchedule.NextExecution);
            if (nextExecution is null)
            {
                _schedules.Remove(runtimeSchedule.Schedule.ScheduleId);
                return;
            }

            if (nextExecution <= runtimeSchedule.NextExecution)
            {
                throw new InvalidOperationException($"Schedule calculator produced an occurrence that does not advance schedule '{runtimeSchedule.Schedule.ScheduleId}'.");
            }

            runtimeSchedule.NextExecution = nextExecution.Value;
        }
    }

    private DateTimeOffset? GetNextExecution()
    {
        DateTimeOffset? nextExecution = null;
        foreach (var schedule in _schedules.Values)
        {
            if (nextExecution is null || schedule.NextExecution < nextExecution)
            {
                nextExecution = schedule.NextExecution;
            }
        }
        return nextExecution;
    }

    private async Task<bool> WaitForNextEventAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        var delayTask = Task.Delay(delay, timeProvider, cancellationToken);
        var signalTask = signal.WaitAsync(cancellationToken);
        var completedTask = await Task.WhenAny(delayTask, signalTask);
        await completedTask;
        return completedTask == signalTask;
    }
}
