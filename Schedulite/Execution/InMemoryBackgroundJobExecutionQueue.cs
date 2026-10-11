using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Schedulite.Execution;

/// <summary>Buffers execution requests in a bounded in-memory channel.</summary>
internal sealed class InMemoryBackgroundJobExecutionQueue
{
    private readonly Channel<BackgroundJobExecutionRequest> _channel;
    private readonly string? _instanceId;
    private readonly int _capacity;
    private readonly ILogger<InMemoryBackgroundJobExecutionQueue> _logger;

    public InMemoryBackgroundJobExecutionQueue(
        string? instanceId,
        int capacity,
        ILogger<InMemoryBackgroundJobExecutionQueue> logger
    ) {
        ArgumentNullException.ThrowIfNull(logger);
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Queue capacity must be greater than zero.");
        }

        _instanceId = instanceId;
        _capacity = capacity;
        _logger = logger;
        _channel = Channel.CreateBounded<BackgroundJobExecutionRequest>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            }
        );
    }

    /// <summary>Writes a request to the bounded queue, waiting when capacity is exhausted.</summary>
    public ValueTask EnqueueAsync(BackgroundJobExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (_channel.Writer.TryWrite(request))
        {
            _logger.LogDebug(
                "Queued {Trigger} execution {ExecutionId} for job {JobId} in Schedulite runtime {InstanceId}.",
                request.Trigger,
                request.ExecutionId,
                request.JobId,
                _instanceId);
            return ValueTask.CompletedTask;
        }

        _logger.LogWarning(
            "Execution queue for Schedulite runtime {InstanceId} reached its capacity of {Capacity}; waiting to queue execution {ExecutionId} for job {JobId}.",
            _instanceId,
            _capacity,
            request.ExecutionId,
            request.JobId);
        return EnqueueAfterBackpressureAsync(request, cancellationToken);
    }

    private async ValueTask EnqueueAfterBackpressureAsync(
        BackgroundJobExecutionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _channel.Writer.WriteAsync(request, cancellationToken);
            _logger.LogDebug(
                "Queued execution {ExecutionId} for job {JobId} after waiting for queue capacity in Schedulite runtime {InstanceId}.",
                request.ExecutionId,
                request.JobId,
                _instanceId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(
                "Canceled while waiting to queue execution {ExecutionId} for job {JobId} in Schedulite runtime {InstanceId}.",
                request.ExecutionId,
                request.JobId,
                _instanceId);
            throw;
        }
    }

    /// <summary>Reads queued requests until the channel is completed or cancellation is requested.</summary>
    public IAsyncEnumerable<BackgroundJobExecutionRequest> ReadAllAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
