namespace Schedulite.Execution;

/// <summary>A disposable class that releases an execution lock when disposed.</summary>
/// <param name="owner">The <see cref="KeyedExecutionLock"/> that owns the lock.</param>
/// <param name="key">The <see cref="ExecutionLockKey"/> that identifies the lock.</param>
/// <param name="entry">The <see cref="ExecutionLockEntry"/> that represents the lock entry.</param>
internal sealed class ExecutionLockReleaser(
    KeyedExecutionLock owner,
    ExecutionLockKey key,
    ExecutionLockEntry entry
) : IDisposable {
    private int _released;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
        {
            owner.Release(key, entry);
        }
    }
}
