namespace Schedulite.Abstractions;

/// <summary>Provides operations for requesting immediate background job executions.</summary>
public interface IBackgroundJobScheduler
{
    /// <summary>Queues a manual execution of a registered job.</summary>
    /// <param name="jobId">The identifier used to register the job.</param>
    /// <param name="subjectId">The identifier of the subject the job should process, or <see langword="null"/> when there is no subject.</param>
    /// <param name="cancellationToken">A token that requests cancellation while queuing the execution.</param>
    /// <returns>A task containing the unique identifier assigned to the queued execution.</returns>
    /// <exception cref="ArgumentException"><paramref name="jobId"/> or a non-null <paramref name="subjectId"/> is empty or whitespace.</exception>
    /// <exception cref="KeyNotFoundException">No job is registered with <paramref name="jobId"/>.</exception>
    /// <exception cref="OperationCanceledException">Queuing was canceled through <paramref name="cancellationToken"/>.</exception>
    public Task<Guid> TriggerAsync(string jobId, string? subjectId, CancellationToken cancellationToken = default);
}
