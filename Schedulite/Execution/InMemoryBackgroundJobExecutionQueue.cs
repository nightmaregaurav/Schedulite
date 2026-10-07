using System.Threading.Channels;

namespace Schedulite.Execution;

internal sealed class InMemoryBackgroundJobExecutionQueue : IBackgroundJobExecutionQueue
{
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

    public ValueTask EnqueueAsync(BackgroundJobExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _channel.Writer.WriteAsync(request, cancellationToken);
    }

    public IAsyncEnumerable<BackgroundJobExecutionRequest> ReadAllAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
