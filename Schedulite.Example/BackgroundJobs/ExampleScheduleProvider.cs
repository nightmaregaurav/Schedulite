using Schedulite.Abstractions;

namespace Schedulite.Example.BackgroundJobs;

public sealed class ExampleScheduleProvider : IBackgroundJobScheduleProvider
{
    public Task<IReadOnlyCollection<BackgroundJobSchedule>> GetSchedulesAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<BackgroundJobSchedule> schedules =
        [
            new()
            {
                ScheduleId = "tenant-1-say-hi",
                JobId = "say-hi",
                SubjectId = "tenant 1",
                ScheduleRule = "every-minute",
                Enabled = true
            },
            new()
            {
                ScheduleId = "tenant-1-skelly",
                JobId = "skelly",
                SubjectId = "tenant 1",
                ScheduleRule = "every-2-minute",
                Enabled = true
            },
            new()
            {
                ScheduleId = "tenant-2-skelly",
                JobId = "skelly",
                SubjectId = "tenant 2",
                ScheduleRule = "every-3-minute",
                Enabled = true
            },
            new()
            {
                ScheduleId = "tenant-3-skelly",
                JobId = "skelly",
                SubjectId = "tenant 3",
                ScheduleRule = "every-4-minute",
                Enabled = false
            }
        ];
        return Task.FromResult(schedules);
    }

    public Task<DateTimeOffset?> GetNextExecution(string scheduleRule, DateTimeOffset now)
    {
        switch (scheduleRule)
        {
            case "every-minute":
                return Task.FromResult<DateTimeOffset?>(now.AddMinutes(1));
            case "every-2-minute":
                return Task.FromResult<DateTimeOffset?>(now.AddMinutes(2));
            case "every-3-minute":
                return Task.FromResult<DateTimeOffset?>(now.AddMinutes(3));
            case "every-4-minute":
                return Task.FromResult<DateTimeOffset?>(now.AddMinutes(4));
            case "every-5-minute":
                return Task.FromResult<DateTimeOffset?>(now.AddMinutes(5));
        }
        throw new ArgumentException($"Unknown schedule rule '{scheduleRule}'.", nameof(scheduleRule));
    }
}
