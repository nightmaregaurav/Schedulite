namespace Schedulite.Abstractions;

/// <summary>Identifies how a background job execution was initiated.</summary>
public enum BackgroundJobTrigger
{
    /// <summary>The execution was created from a recurring schedule.</summary>
    Scheduled,
    /// <summary>The execution was requested directly through <see cref="IBackgroundJobScheduler"/>.</summary>
    Manual
}
