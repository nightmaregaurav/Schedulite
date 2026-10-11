using Microsoft.Extensions.Logging;
using Schedulite.Abstractions;

namespace Schedulite.Example.BackgroundJobs.Services;

public sealed class SayHiJob(TimeProvider timeProvider, ILogger<SayHiJob> logger) : IBackgroundJob
{
    public ExecutionConcurrencyScope ConcurrencyScope => ExecutionConcurrencyScope.PerJob;
    public string JobName => "Say Hi";
    public string JobDescription => "Says hi to the console and waits for 5 seconds to simulate work, then says bye.";

    public async Task ExecuteAsync(BackgroundJobContext context, CancellationToken cancellationToken)
    {
        logger.LogInformation("Saying hi to subject {SubjectId} for execution {ExecutionId} at {TimeUtc}.", context.SubjectId, context.ExecutionId, timeProvider.GetUtcNow());
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        logger.LogInformation("Finished saying hi to subject {SubjectId} for execution {ExecutionId} at {TimeUtc}.", context.SubjectId, context.ExecutionId, timeProvider.GetUtcNow());
    }
}
