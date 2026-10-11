using Schedulite.Abstractions;

namespace Schedulite.Example.BackgroundJobs.Providers;

/// <summary>Supplies a separate daily schedule for the named maintenance runtime.</summary>
public sealed class MaintenanceScheduleProvider : IBackgroundJobScheduleProvider
{
    public Task<IReadOnlyCollection<BackgroundJobSchedule>> GetSchedulesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyCollection<BackgroundJobSchedule> schedules =
        [
            new()
            {
                ScheduleId = "tenant-1-expired-data-cleanup",
                JobId = "cleanup-expired-data",
                SubjectId = "tenant-1",
                ScheduleRule = "daily-at-2am-utc"
            },
            new()
            {
                ScheduleId = "tenant-2-expired-data-cleanup",
                JobId = "cleanup-expired-data",
                SubjectId = "tenant-2",
                ScheduleRule = "daily-at-2am-utc"
            }
        ];

        return Task.FromResult(schedules);
    }

    public Task<DateTimeOffset?> GetNextExecution(string scheduleRule, DateTimeOffset now)
    {
        if (!StringComparer.Ordinal.Equals(scheduleRule, "daily-at-2am-utc"))
        {
            throw new ArgumentException($"Unknown maintenance schedule rule '{scheduleRule}'.", nameof(scheduleRule));
        }

        var next = new DateTimeOffset(now.Year, now.Month, now.Day, 2, 0, 0, TimeSpan.Zero);
        if (next <= now)
        {
            next = next.AddDays(1);
        }

        return Task.FromResult<DateTimeOffset?>(next);
    }
}
