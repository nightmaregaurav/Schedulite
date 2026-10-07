namespace Schedulite.Abstractions;

public interface IBackgroundJobScheduler
{
    public Task<Guid> TriggerAsync(string jobId, string subjectId, CancellationToken cancellationToken = default);
}
