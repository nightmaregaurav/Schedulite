using Schedulite.Abstractions;

namespace Schedulite.Scheduling;

internal sealed class BackgroundJobRegistry
{
    private readonly IReadOnlyDictionary<string, Type> _jobTypes;

    public BackgroundJobRegistry(IEnumerable<BackgroundJobRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        var registry = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var registration in registrations)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(registration.Id);
            ArgumentNullException.ThrowIfNull(registration.JobType);

            if (!typeof(IBackgroundJob).IsAssignableFrom(registration.JobType))
            {
                throw new InvalidOperationException($"Type '{registration.JobType.FullName}' does not implement {nameof(IBackgroundJob)}.");
            }

            if (!registry.TryAdd(registration.Id, registration.JobType))
            {
                throw new InvalidOperationException($"A background job with ID '{registration.Id}' is already registered.");
            }
        }
        _jobTypes = registry;
    }

    public Type GetJobType(string jobId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        if (!_jobTypes.TryGetValue(jobId, out var jobType))
        {
            throw new KeyNotFoundException($"No background job with ID '{jobId}' is registered.");
        }

        return jobType;
    }

    public bool Contains(string jobId)
    {
        return !string.IsNullOrWhiteSpace(jobId) && _jobTypes.ContainsKey(jobId);
    }
}
