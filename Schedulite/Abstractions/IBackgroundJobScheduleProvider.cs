namespace Schedulite.Abstractions;

/// <summary>Loads recurring schedules and interprets their application-defined rules.</summary>
public interface IBackgroundJobScheduleProvider
{
    /// <summary>Loads the schedules currently configured by the application.</summary>
    /// <param name="cancellationToken">A token that requests cancellation of the load.</param>
    /// <returns>A task containing the current schedules; disabled entries may be returned and are ignored by the scheduler.</returns>
    /// <exception cref="OperationCanceledException">The load was canceled through <paramref name="cancellationToken"/>.</exception>
    public Task<IReadOnlyCollection<BackgroundJobSchedule>> GetSchedulesAsync(CancellationToken cancellationToken = default);
    /// <summary>Calculates the next occurrence of a rule after the supplied time.</summary>
    /// <param name="scheduleRule">The application-defined rule to interpret.</param>
    /// <param name="now">The reference time from which to calculate the next occurrence.</param>
    /// <returns>A task containing the next occurrence, or <see langword="null"/> when the rule has no future occurrence.</returns>
    /// <exception cref="ArgumentException">The rule is invalid or unsupported.</exception>
    /// <exception cref="OperationCanceledException">Implementations may throw this when their calculation is canceled.</exception>
    public Task<DateTimeOffset?> GetNextExecution(string scheduleRule, DateTimeOffset now);
}
