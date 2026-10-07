namespace Schedulite.Configuration;

/// <summary>Configures the concurrency and buffering limits used by Schedulite.</summary>
public sealed class ScheduliteOptions
{
    /// <summary>Gets or sets the maximum number of jobs that may execute concurrently. Defaults to <c>4</c>.</summary>
    public int MaxConcurrency { get; set; } = 4;
    /// <summary>Gets or sets the maximum number of execution requests buffered before producers wait. Defaults to <c>100</c>.</summary>
    public int ExecutionQueueCapacity { get; set; } = 100;

    /// <summary>Checks that all configured limits are positive.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="MaxConcurrency"/> or <see cref="ExecutionQueueCapacity"/> is zero or negative.</exception>
    internal void Validate()
    {
        if (MaxConcurrency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxConcurrency), MaxConcurrency, "The maximum concurrency must be greater than zero.");
        }

        if (ExecutionQueueCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ExecutionQueueCapacity), ExecutionQueueCapacity, "Execution queue capacity must be greater than zero.");
        }
    }
}
