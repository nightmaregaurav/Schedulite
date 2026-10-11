using Microsoft.Extensions.Logging;
using Schedulite.Abstractions;

namespace Schedulite.Example.BackgroundJobs.Services;

/// <summary>A manually triggered example of work that does not need a recurring schedule.</summary>
public sealed class SendDigestJob(ILogger<SendDigestJob> logger, TimeProvider timeProvider) : IBackgroundJob
{
    public string JobName => "Send digest";
    public string JobDescription => "Sends an on-demand digest for one subject.";
    public ExecutionConcurrencyScope ConcurrencyScope => ExecutionConcurrencyScope.PerSubject;

    public Task ExecuteAsync(BackgroundJobContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation(
            "Sending a digest for subject {SubjectId}; triggered by {Trigger} with execution {ExecutionId} at {TimeUtc}.",
            context.SubjectId,
            context.Trigger,
            context.ExecutionId,
            timeProvider.GetUtcNow());
        return Task.CompletedTask;
    }
}
