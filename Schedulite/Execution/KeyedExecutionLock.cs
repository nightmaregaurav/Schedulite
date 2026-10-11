namespace Schedulite.Execution;

/// <summary>Provides per-key asynchronous locks and removes idle keys to bound memory use.</summary>
internal sealed class KeyedExecutionLock
{
    // The gate lock to prevent concurrent access to the '_entries' dictionary.
    // This is necessary because the dictionary is not thread-safe, and we need to ensure that only one thread can modify it at a time.
    private readonly Lock _gate = new();
    // The dictionary of lock entries, keyed by ExecutionLockKey. Each entry contains a semaphore and a reference count.
    private readonly Dictionary<ExecutionLockKey, ExecutionLockEntry> _entries = new();

    public async ValueTask<IDisposable> AcquireAsync(ExecutionLockKey key, CancellationToken cancellationToken)
    {
        ExecutionLockEntry entry;
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out entry!))
            {
                entry = new ExecutionLockEntry();
                _entries.Add(key, entry);
            }

            entry.References++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
            return new ExecutionLockReleaser(this, key, entry);
        }
        catch
        {
            ReleaseReference(key, entry);
            throw;
        }
    }

    internal void Release(ExecutionLockKey key, ExecutionLockEntry entry)
    {
        entry.Semaphore.Release();
        ReleaseReference(key, entry);
    }

    private void ReleaseReference(ExecutionLockKey key, ExecutionLockEntry entry)
    {
        lock (_gate)
        {
            entry.References--;
            if (entry.References == 0)
            {
                _entries.Remove(key);
                entry.Semaphore.Dispose();
            }
        }
    }
}
