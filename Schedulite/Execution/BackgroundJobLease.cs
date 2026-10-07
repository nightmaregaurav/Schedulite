using Microsoft.Extensions.DependencyInjection;
using Schedulite.Abstractions;

namespace Schedulite.Execution;

internal sealed class BackgroundJobLease(AsyncServiceScope scope, IBackgroundJob job)
{
    public IBackgroundJob Job { get; } = job;

    public ValueTask DisposeAsync()
    {
        return scope.DisposeAsync();
    }
}
