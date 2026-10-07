namespace Schedulite.Abstractions;

public interface IBackgroundJobScheduleProvider
{
    public Task<IReadOnlyCollection<BackgroundJobSchedule>> GetSchedulesAsync(CancellationToken cancellationToken = default);
    public Task<DateTimeOffset?> GetNextExecution(string scheduleRule, DateTimeOffset now);
}
