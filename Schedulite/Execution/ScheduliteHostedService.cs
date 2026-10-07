using Microsoft.Extensions.Hosting;
using Schedulite.Scheduling;

namespace Schedulite.Execution;

internal sealed class ScheduliteHostedService(BackgroundJobScheduler scheduler, BackgroundJobDispatcher dispatcher) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var runtimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        var runtimeToken = runtimeCancellation.Token;

        var schedulerTask = scheduler.RunAsync(runtimeToken);
        var dispatcherTask = dispatcher.RunAsync(runtimeToken);

        try
        {
            var completedTask = await Task.WhenAny(schedulerTask, dispatcherTask);

            // If either runtime component stops unexpectedly,
            // stop the other one as well.
            await runtimeCancellation.CancelAsync();
            await completedTask;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
        finally
        {
            await runtimeCancellation.CancelAsync();
            try
            {
                await Task.WhenAll(schedulerTask, dispatcherTask);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal host shutdown.
            }
        }
    }
}
