using System.Threading.Channels;

namespace Schedulite.Execution;

/// <summary>Buffers execution requests in a bounded in-memory channel.</summary>
internal sealed class InMemoryBackgroundJobExecutionQueue : IBackgroundJobExecutionQueue
{
    /// <summary>Gets the bounded channel that stores pending execution requests.</summary>
    private readonly Channel<BackgroundJobExecutionRequest> _channel;

    public InMemoryBackgroundJobExecutionQueue(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Queue capacity must be greater than zero.");
        }

        _channel = Channel.CreateBounded<BackgroundJobExecutionRequest>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            }
        );
    }

    /// <summary>Writes a request to the bounded queue, waiting when capacity is exhausted.</summary>
    public ValueTask EnqueueAsync(BackgroundJobExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _channel.Writer.WriteAsync(request, cancellationToken);
    }

    /// <summary>Reads queued requests until the channel is completed or cancellation is requested.</summary>
    public IAsyncEnumerable<BackgroundJobExecutionRequest> ReadAllAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
