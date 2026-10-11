using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Schedulite.Scheduling;

namespace Schedulite.Execution;

/// <summary>Runs the scheduler and dispatcher as coordinated hosted background tasks.</summary>
internal sealed class ScheduliteHostedService(
    string? instanceId,
    BackgroundJobScheduler scheduler,
    BackgroundJobDispatcher dispatcher,
    ILogger<ScheduliteHostedService> logger) : BackgroundService
{
    /// <summary>Coordinates scheduler and dispatcher lifetimes until shutdown.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Starting Schedulite hosted runtime {InstanceId}.", instanceId);
        using var runtimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var runtimeToken = runtimeCancellation.Token;

        var schedulerTask = scheduler.RunAsync(runtimeToken);
        var dispatcherTask = dispatcher.RunAsync(runtimeToken);
        Exception? failure = null;
        try
        {
            var completedTask = await Task.WhenAny(schedulerTask, dispatcherTask);
            if (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await completedTask;
                }
                catch (Exception exception)
                {
                    failure = exception;
                }

                failure ??= new InvalidOperationException("A Schedulite runtime component stopped before host shutdown.");
            }
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
            catch (Exception exception)
            {
                failure ??= exception;
            }

            if (failure is not null)
            {
                logger.LogCritical(failure, "Schedulite hosted runtime {InstanceId} stopped unexpectedly.", instanceId);
                throw new InvalidOperationException("Schedulite hosted runtime stopped unexpectedly.", failure);
            }

            logger.LogInformation("Stopped Schedulite hosted runtime {InstanceId}.", instanceId);
        }
    }
}
