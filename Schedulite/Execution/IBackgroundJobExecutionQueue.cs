namespace Schedulite.Execution;

/// <summary>Defines the internal asynchronous queue contract for job execution requests.</summary>
internal interface IBackgroundJobExecutionQueue
{
    /// <summary>Writes a request to the bounded queue, waiting when capacity is exhausted.</summary>
    internal ValueTask EnqueueAsync(BackgroundJobExecutionRequest request, CancellationToken cancellationToken);
    /// <summary>Reads queued requests until the channel is completed or cancellation is requested.</summary>
    internal IAsyncEnumerable<BackgroundJobExecutionRequest> ReadAllAsync(CancellationToken cancellationToken);
}
