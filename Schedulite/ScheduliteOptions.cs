namespace Schedulite;

public sealed class ScheduliteOptions
{
    public int MaxConcurrency { get; set; } = 4;
    public int ExecutionQueueCapacity { get; set; } = 100;

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
