namespace Schedulite.Execution;

/// <summary>Tracks the job IDs registered for one Schedulite runtime.</summary>
internal sealed class BackgroundJobRegistry
{
    private readonly HashSet<string> _jobIds = new(StringComparer.Ordinal);

    /// <summary>Returns the job IDs registered for this runtime.</summary>
    public IReadOnlyCollection<string> JobIds => _jobIds;

    /// <summary>Registers a job ID and rejects duplicates.</summary>
    public void Register(string jobId)
    {
        if (!_jobIds.Add(jobId))
        {
            throw new ArgumentException($"A background job with ID '{jobId}' is already registered for this Schedulite runtime.", nameof(jobId));
        }
    }

    /// <summary>Checks whether an ID belongs to a registered job.</summary>
    public bool Contains(string jobId)
    {
        return _jobIds.Contains(jobId);
    }
}
