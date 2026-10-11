using Microsoft.Extensions.Hosting;
using Schedulite.Scheduling;

namespace Schedulite.Execution;

/// <summary>Runs the scheduler and dispatcher as coordinated hosted background tasks.</summary>
internal sealed class ScheduliteHostedService(BackgroundJobScheduler scheduler, BackgroundJobDispatcher dispatcher) : BackgroundService
{
    /// <summary>Coordinates scheduler and dispatcher lifetimes until shutdown.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var runtimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var runtimeToken = runtimeCancellation.Token;

        var schedulerTask = scheduler.RunAsync(runtimeToken);
        var dispatcherTask = dispatcher.RunAsync(runtimeToken);
        try
        {
            // If either runtime component stops unexpectedly or OperationCanceledException is thrown,
            // we jump to finally to gracefully exit
            await Task.WhenAny(schedulerTask, dispatcherTask);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
        finally
        {
            // Ensure the runtime is properly canceled
            await runtimeCancellation.CancelAsync();
            try
            {
                // We sent the cancellation signal using token already, so these tasks should complete quickly.
                // We will wait for them to ensure proper cleanup
                await Task.WhenAll(schedulerTask, dispatcherTask);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal host shutdown.
            }
        }
    }
}
