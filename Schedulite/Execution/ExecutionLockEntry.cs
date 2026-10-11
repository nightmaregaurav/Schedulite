namespace Schedulite.Execution;

/// <summary>Execution lock entry for a job and optional subject. Used to limit concurrent executions of the same job or subject based on defined rule.</summary>
internal sealed class ExecutionLockEntry
{
    // The semaphore is initialized with a count of 1, which means that only one thread can enter the critical section at a time. The maximum count is also set to 1, which means that no more than one thread can be waiting to enter the critical section at any given time.
    public SemaphoreSlim Semaphore { get; } = new(1, 1);
    // The number of references to this lock entry. This is used to determine when to remove the lock entry from the dictionary.
    public int References { get; set; }
}
