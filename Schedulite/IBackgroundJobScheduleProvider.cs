namespace Schedulite;

public interface IBackgroundJobScheduleProvider
{
    Task<IReadOnlyCollection<BackgroundJobSchedule>> GetSchedulesAsync(CancellationToken cancellationToken = default);
    Task<DateTimeOffset?> GetNextExecution(string scheduleRule, DateTimeOffset now);
}
