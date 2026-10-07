namespace Schedulite.Execution;

internal interface IBackgroundJobExecutionQueue
{
    internal ValueTask EnqueueAsync(BackgroundJobExecutionRequest request, CancellationToken cancellationToken);
    internal IAsyncEnumerable<BackgroundJobExecutionRequest> ReadAllAsync(CancellationToken cancellationToken);
}
