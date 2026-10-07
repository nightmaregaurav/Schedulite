namespace Schedulite.Abstractions;

/// <summary>Represents a unit of work that Schedulite can execute in the background.</summary>
public interface IBackgroundJob
{
    /// <summary>Gets the human-readable name of this job.</summary>
    public string JobName { get; }
    /// <summary>Gets a human-readable description of this job.</summary>
    public string JobDescription { get; }
    /// <summary>Executes the job for the supplied context.</summary>
    /// <param name="context">The identifiers and trigger details for this execution.</param>
    /// <param name="cancellationToken">A token that requests cancellation of the execution.</param>
    /// <returns>A task that represents the asynchronous execution.</returns>
    /// <exception cref="ArgumentNullException">The implementation may throw this exception when <paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled through <paramref name="cancellationToken"/>.</exception>
    public Task ExecuteAsync(BackgroundJobContext context, CancellationToken cancellationToken);
}
