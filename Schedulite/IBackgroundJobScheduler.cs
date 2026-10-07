namespace Schedulite;

public interface IBackgroundJobScheduler
{
    Task TriggerAsync(string jobId, string subjectId, CancellationToken cancellationToken = default);
}
