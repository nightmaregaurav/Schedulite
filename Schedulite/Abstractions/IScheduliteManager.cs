namespace Schedulite.Abstractions;

/// <summary>Provides unkeyed access to jobs and scheduling operations across all Schedulite runtimes.</summary>
public interface IScheduliteManager
{
    /// <summary>Lists every runtime with its registered jobs and currently configured schedules.</summary>
    /// <param name="cancellationToken">A token that requests cancellation of provider calls.</param>
    /// <returns>The runtimes and their jobs, grouped by runtime.</returns>
    public Task<IReadOnlyCollection<ScheduliteRuntimeInfo>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Queues a manual execution on the specified runtime.</summary>
    /// <param name="instanceId">The runtime ID, or <see langword="null"/> for the default runtime.</param>
    /// <param name="jobId">The identifier used to register the job in that runtime.</param>
    /// <param name="subjectId">The identifier of the subject the job should process, or <see langword="null"/> when there is no subject.</param>
    /// <param name="cancellationToken">A token that requests cancellation while queuing the execution.</param>
    /// <returns>A task containing the unique identifier assigned to the queued execution.</returns>
    /// <exception cref="KeyNotFoundException">The runtime or job is not registered.</exception>
    public Task<Guid> TriggerAsync(string? instanceId, string jobId, string? subjectId = null, CancellationToken cancellationToken = default);

    /// <summary>Requests a schedule reload on every registered runtime.</summary>
    /// <param name="cancellationToken">A token that can cancel the request before it is signaled.</param>
    /// <returns>A task that completes when all reload requests have been signaled.</returns>
    public Task ReloadAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Requests a schedule reload on one runtime.</summary>
    /// <param name="instanceId">The runtime ID, or <see langword="null"/> for the default runtime.</param>
    /// <param name="cancellationToken">A token that can cancel the request before it is signaled.</param>
    /// <returns>A task that completes when the reload request has been signaled.</returns>
    /// <exception cref="KeyNotFoundException">The runtime is not registered.</exception>
    public Task ReloadAsync(string? instanceId, CancellationToken cancellationToken = default);
}
