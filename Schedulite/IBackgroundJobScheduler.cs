namespace Schedulite;

public interface IBackgroundJobScheduler
{
    public Task TriggerAsync(string jobId, string subjectId, CancellationToken cancellationToken = default);
}
