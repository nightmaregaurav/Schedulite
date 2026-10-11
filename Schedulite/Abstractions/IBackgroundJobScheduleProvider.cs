namespace Schedulite.Abstractions;

/// <summary>Loads recurring schedules and interprets their application-defined rules.</summary>
/// <remarks>
/// Schedulite resolves the provider in a fresh asynchronous dependency injection scope for each call.
/// Implementations should keep durable or shared state in their backing services rather than in provider instance fields.
/// </remarks>
public interface IBackgroundJobScheduleProvider
{
    /// <summary>Loads the schedules currently configured by the application.</summary>
    /// <param name="cancellationToken">A token that requests cancellation of the load.</param>
    /// <returns>A task containing the current schedules; disabled entries are not scheduled, but schedule IDs must still be unique within the runtime.</returns>
    /// <exception cref="OperationCanceledException">The load was canceled through <paramref name="cancellationToken"/>.</exception>
    public Task<IReadOnlyCollection<BackgroundJobSchedule>> GetSchedulesAsync(CancellationToken cancellationToken = default);
    /// <summary>Calculates the next occurrence of a rule after the supplied time.</summary>
    /// <param name="scheduleRule">The application-defined rule to interpret.</param>
    /// <param name="now">The reference time from which to calculate the next occurrence.</param>
    /// <returns>A task containing the next occurrence, or <see langword="null"/> when the rule has no future occurrence.</returns>
    /// <exception cref="ArgumentException">The rule is invalid or unsupported.</exception>
    /// <remarks>
    /// This method has no cancellation token, so calculations should complete promptly. When Schedulite asks for the
    /// occurrence after the currently queued one, returning that same timestamp identifies a one-time schedule:
    /// Schedulite executes the queued occurrence once, logs the one-time schedule, and removes it from the active queue.
    /// The provider remains responsible for deciding which schedules and occurrences are available across launches or
    /// application instances.
    /// </remarks>
    public Task<DateTimeOffset?> GetNextExecution(string scheduleRule, DateTimeOffset now);
}
