using Microsoft.Extensions.DependencyInjection;

namespace Schedulite;

internal sealed class BackgroundJobLease(AsyncServiceScope scope, IBackgroundJob job)
{
    public IBackgroundJob Job { get; } = job;

    public ValueTask DisposeAsync()
    {
        return scope.DisposeAsync();
    }
}
