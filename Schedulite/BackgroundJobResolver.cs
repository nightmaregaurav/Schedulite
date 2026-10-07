using Microsoft.Extensions.DependencyInjection;

namespace Schedulite;

internal sealed class BackgroundJobResolver(IServiceScopeFactory scopeFactory, BackgroundJobRegistry registry)
{
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
