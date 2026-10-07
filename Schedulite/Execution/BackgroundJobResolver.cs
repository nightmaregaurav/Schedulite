using Microsoft.Extensions.DependencyInjection;
using Schedulite.Abstractions;
using Schedulite.Scheduling;

namespace Schedulite.Execution;

/// <summary>Resolves a registered job within a fresh asynchronous dependency injection scope.</summary>
internal sealed class BackgroundJobResolver(IServiceScopeFactory scopeFactory, BackgroundJobRegistry registry)
{
    /// <summary>Creates a scope, resolves the registered job, and returns a lease that owns the scope.</summary>
    public ValueTask<BackgroundJobLease> ResolveAsync(string jobId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var scope = scopeFactory.CreateAsyncScope();

        try
        {
            var jobType = registry.GetJobType(jobId);
            var job = scope.ServiceProvider.GetRequiredService(jobType);

            if (job is not IBackgroundJob backgroundJob)
            {
                throw new InvalidOperationException(
                    $"Registered type '{jobType.FullName}' " +
                    $"does not implement {nameof(IBackgroundJob)}.");
            }

            return ValueTask.FromResult(
                new BackgroundJobLease(
                    scope,
                    backgroundJob));
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }
}
