namespace Schedulite;

internal interface IBackgroundJobExecutionQueue
{
    ValueTask EnqueueAsync(BackgroundJobExecutionRequest request, CancellationToken cancellationToken);
    IAsyncEnumerable<BackgroundJobExecutionRequest> ReadAllAsync(CancellationToken cancellationToken);
}
