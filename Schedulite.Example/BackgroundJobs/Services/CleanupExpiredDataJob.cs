using Microsoft.Extensions.Logging;
using Schedulite.Abstractions;

namespace Schedulite.Example.BackgroundJobs.Services;

/// <summary>Demonstrates tenant-aware, serialized work in the maintenance runtime.</summary>
public sealed class CleanupExpiredDataJob(ILogger<CleanupExpiredDataJob> logger, TimeProvider timeProvider) : IBackgroundJob
{
    public string JobName => "Cleanup expired data";
    public string JobDescription => "Cleans expired data for a tenant once per day.";
    public ExecutionConcurrencyScope ConcurrencyScope => ExecutionConcurrencyScope.PerSubject;

    public async Task ExecuteAsync(BackgroundJobContext context, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Starting expired-data cleanup for subject {SubjectId} in runtime {InstanceId}, execution {ExecutionId}.",
            context.SubjectId,
            context.InstanceId,
            context.ExecutionId);

        await Task.Delay(TimeSpan.FromSeconds(2), timeProvider, cancellationToken);

        logger.LogInformation(
            "Completed expired-data cleanup for subject {SubjectId} at {TimeUtc}.",
            context.SubjectId,
            timeProvider.GetUtcNow());
    }
}
