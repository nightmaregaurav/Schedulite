using Microsoft.Extensions.DependencyInjection;
using Schedulite.Abstractions;
using Schedulite.Scheduling;

namespace Schedulite.Execution;

/// <summary>Routes unkeyed management requests to the corresponding keyed runtime services.</summary>
internal sealed class ScheduliteManager(
    IServiceProvider serviceProvider,
    IServiceScopeFactory scopeFactory,
    IEnumerable<ScheduliteRuntimeRegistration> registrations) : IScheduliteManager
{
    private readonly ScheduliteRuntimeRegistration[] _registrations = registrations.ToArray();

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<ScheduliteRuntimeInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var runtimes = new List<ScheduliteRuntimeInfo>(_registrations.Length);
        foreach (var registration in _registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            runtimes.AddRange(await GetRuntimeInfoAsync(registration, cancellationToken));
        }

        return runtimes;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<ScheduliteRuntimeInfo>> ListAsync(string? instanceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var registration = _registrations.FirstOrDefault(r => r.InstanceId == instanceId);
        if(registration is null)
        {
            throw new KeyNotFoundException($"Schedulite runtime '{instanceId ?? "<default>"}' is not registered.");
        }

        return await GetRuntimeInfoAsync(registration, cancellationToken);
    }

    private async Task<IReadOnlyCollection<ScheduliteRuntimeInfo>> GetRuntimeInfoAsync(ScheduliteRuntimeRegistration registration, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var scope = scopeFactory.CreateAsyncScope();
        var scheduleProvider = scope.ServiceProvider.GetRequiredKeyedService<IBackgroundJobScheduleProvider>(registration.InstanceId);
        var schedules = await scheduleProvider.GetSchedulesAsync(cancellationToken);
        ArgumentNullException.ThrowIfNull(schedules);
        var scheduler = serviceProvider.GetRequiredKeyedService<BackgroundJobScheduler>(registration.InstanceId);

        var backgroundJobModels = new List<BackgroundJobInfoModel>(schedules.Count);
        foreach (var s in schedules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isQueued = scheduler.TryGetNextExecution(s.ScheduleId, out var nextExecution);
            backgroundJobModels.Add(new BackgroundJobInfoModel
            {
                ScheduliteInstanceId = registration.InstanceId,
                ScheduleId = s.ScheduleId,
                JobId = s.JobId,
                SubjectId = s.SubjectId,
                ScheduleRule = s.ScheduleRule,
                Enabled = s.Enabled,
                NotQueued = !isQueued,
                NextExecution = isQueued ? nextExecution : null
            });
        }

        return new List<ScheduliteRuntimeInfo>
        {
            new()
            {
                InstanceId = registration.InstanceId,
                Schedules = backgroundJobModels
            }
        };
    }

    /// <inheritdoc />
    public Task<Guid> TriggerAsync(string? instanceId, string jobId, string? subjectId = null, CancellationToken cancellationToken = default)
    {
        var registration = GetRegistration(instanceId);
        return GetScheduler(registration).TriggerAsync(jobId, subjectId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task ReloadAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var registration in _registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await GetScheduler(registration).ReloadSchedulesAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public Task ReloadAsync(string? instanceId, CancellationToken cancellationToken = default)
    {
        return GetScheduler(GetRegistration(instanceId)).ReloadSchedulesAsync(cancellationToken);
    }

    private IBackgroundJobScheduler GetScheduler(ScheduliteRuntimeRegistration registration)
    {
        return serviceProvider.GetRequiredKeyedService<IBackgroundJobScheduler>(registration.InstanceId);
    }

    private ScheduliteRuntimeRegistration GetRegistration(string? instanceId)
    {
        foreach (var registration in _registrations)
        {
            if (StringComparer.Ordinal.Equals(registration.InstanceId, instanceId))
            {
                return registration;
            }
        }

        throw new KeyNotFoundException($"Schedulite runtime '{instanceId ?? "<default>"}' is not registered.");
    }
}
