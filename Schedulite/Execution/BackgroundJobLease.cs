using Microsoft.Extensions.DependencyInjection;
using Schedulite.Abstractions;

namespace Schedulite.Execution;

/// <summary>Owns a resolved job instance and the dependency injection scope that contains it.</summary>
internal sealed class BackgroundJobLease(AsyncServiceScope scope, IBackgroundJob job)
{
    /// <summary>Gets the resolved job instance owned by this lease.</summary>
    public IBackgroundJob Job { get; } = job;

    /// <summary>Disposes the asynchronous scope that owns the resolved job.</summary>
    public ValueTask DisposeAsync()
    {
        return scope.DisposeAsync();
    }
}
