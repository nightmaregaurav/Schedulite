using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Schedulite.Abstractions;
using Schedulite.Configuration;
using Schedulite.Execution;
using Schedulite.Scheduling;

namespace Schedulite.DependencyInjection;

/// <summary>Registers Schedulite runtimes and jobs with dependency injection.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers the default runtime using the null dependency-injection key.</summary>
    /// <typeparam name="TProvider">The application implementation that supplies recurring schedules.</typeparam>
    /// <param name="services">The application's service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The default runtime is already registered.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A configured limit is not positive.</exception>
    public static IServiceCollection AddSchedulite<TProvider>(this IServiceCollection services) where TProvider : class, IBackgroundJobScheduleProvider
    {
        ArgumentNullException.ThrowIfNull(services);
        return AddScheduliteCore<TProvider>(services, null, new ScheduliteOptions());
    }

    /// <summary>Registers the default runtime using the null dependency-injection key.</summary>
    /// <typeparam name="TProvider">The application implementation that supplies recurring schedules.</typeparam>
    /// <param name="services">The application's service collection.</param>
    /// <param name="options">Schedulite runtime configuration options.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The default runtime is already registered.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A configured limit is not positive.</exception>
    public static IServiceCollection AddSchedulite<TProvider>(this IServiceCollection services, ScheduliteOptions options) where TProvider : class, IBackgroundJobScheduleProvider
    {
        ArgumentNullException.ThrowIfNull(services);
        return AddScheduliteCore<TProvider>(services, null, options);
    }

    /// <summary>Registers a named runtime with its own schedules, jobs, limits, and queue.</summary>
    /// <typeparam name="TProvider">The application implementation that supplies recurring schedules.</typeparam>
    /// <param name="services">The application's service collection.</param>
    /// <param name="instanceId">The key used to select this runtime's services.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="instanceId"/> is blank or already registered.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A configured limit is not positive.</exception>
    public static IServiceCollection AddSchedulite<TProvider>(this IServiceCollection services, string instanceId) where TProvider : class, IBackgroundJobScheduleProvider
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        return AddScheduliteCore<TProvider>(services, instanceId, new ScheduliteOptions());
    }

    /// <summary>Registers a named runtime with its own schedules, jobs, limits, and queue.</summary>
    /// <typeparam name="TProvider">The application implementation that supplies recurring schedules.</typeparam>
    /// <param name="services">The application's service collection.</param>
    /// <param name="instanceId">The key used to select this runtime's services.</param>
    /// <param name="options">Schedulite runtime configuration options.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="instanceId"/> is blank or already registered.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A configured limit is not positive.</exception>
    public static IServiceCollection AddSchedulite<TProvider>(this IServiceCollection services, string instanceId, ScheduliteOptions options) where TProvider : class, IBackgroundJobScheduleProvider
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        return AddScheduliteCore<TProvider>(services, instanceId, options);
    }

    /// <summary>Registers a job in the default runtime under the null dependency-injection key.</summary>
    /// <typeparam name="TJob">The concrete job implementation.</typeparam>
    /// <param name="services">The application's service collection.</param>
    /// <param name="jobId">The unique identifier for this job in the runtime.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="jobId"/> is blank or already registered, or <typeparamref name="TJob"/> is abstract or open generic.</exception>
    /// <exception cref="InvalidOperationException">The default runtime has not been registered.</exception>
    public static IServiceCollection AddScheduliteJob<TJob>(this IServiceCollection services, string jobId) where TJob : class, IBackgroundJob
    {
        ArgumentNullException.ThrowIfNull(services);
        return AddScheduliteJobCore<TJob>(services, null, jobId);
    }

    /// <summary>Registers a job in the named runtime.</summary>
    /// <typeparam name="TJob">The concrete job implementation.</typeparam>
    /// <param name="services">The application's service collection.</param>
    /// <param name="instanceId">The key of the runtime that owns the job.</param>
    /// <param name="jobId">The unique identifier for this job in the runtime.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="instanceId"/> or <paramref name="jobId"/> is blank, the job ID is already registered in that runtime, or <typeparamref name="TJob"/> is abstract or open generic.</exception>
    /// <exception cref="InvalidOperationException">The named runtime has not been registered.</exception>
    public static IServiceCollection AddScheduliteJob<TJob>(this IServiceCollection services, string instanceId, string jobId) where TJob : class, IBackgroundJob
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        return AddScheduliteJobCore<TJob>(services, instanceId, jobId);
    }

    private static IServiceCollection AddScheduliteCore<TProvider>(IServiceCollection services, string? instanceId, ScheduliteOptions options) where TProvider : class, IBackgroundJobScheduleProvider
    {
        // The registry is the runtime marker; check it before adding any of this runtime's services.
        if (services
            .Any(descriptor =>
                descriptor.ServiceType == typeof(BackgroundJobRegistry) &&
                (descriptor.IsKeyedService ? Equals(descriptor.ServiceKey, instanceId) : instanceId is null)
            )
        ) {
            throw new ArgumentException($"A Schedulite runtime with ID '{instanceId ?? "<null>"}' is already registered.", nameof(instanceId));
        }

        // prepare the option object and validate it before registering any runtime services
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        // inject the option object as singleton so it can be accessed later
        services.AddKeyedSingleton(instanceId, options);

        // Keep the runtime's registered IDs independently from job construction so manual triggers can be validated eagerly.
        services.AddKeyedSingleton(instanceId, new BackgroundJobRegistry());

        // The clock is a shared application infrastructure.
        // This can be overridden by the application if desired,
        // in which case the runtime will use the overridden clock.
        // This is not keyed because the clock is not runtime-specific.
        services.TryAddSingleton(TimeProvider.System);

        // Enable logging, the configuration for this is inherited from the application's configuration
        services.AddLogging();

        // The schedule provider that fetches recurring schedules and evaluates the next execution for this runtime.
        services.AddKeyedScoped<IBackgroundJobScheduleProvider, TProvider>(instanceId);

        // The in-memory queue for background job execution.
        services.AddKeyedSingleton<InMemoryBackgroundJobExecutionQueue>(instanceId, (provider, _) =>
            new InMemoryBackgroundJobExecutionQueue(
                instanceId,
                options.ExecutionQueueCapacity,
                provider.GetRequiredService<ILogger<InMemoryBackgroundJobExecutionQueue>>()
            )
        );

        // The signal that wakes up the scheduler when a new schedule is added.
        services.AddKeyedSingleton<SchedulerSignal>(instanceId, (_, _) => new SchedulerSignal());

        // The registry that tracks the jobs registered in this runtime.
        services.AddKeyedSingleton<BackgroundJobScheduler>(instanceId, (provider, _) =>
            new BackgroundJobScheduler(
                instanceId,
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredKeyedService<InMemoryBackgroundJobExecutionQueue>(instanceId),
                provider.GetRequiredKeyedService<SchedulerSignal>(instanceId),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredKeyedService<BackgroundJobRegistry>(instanceId),
                provider.GetRequiredService<ILogger<BackgroundJobScheduler>>()
            )
        );
        // To expose key management endpoint like triggering a job or refreshing the schedule
        services.AddKeyedSingleton<IBackgroundJobScheduler>(instanceId, (provider, _) =>
            provider.GetRequiredKeyedService<BackgroundJobScheduler>(instanceId)
        );

        // The dispatcher that consumes the execution queue and runs jobs.
        services.AddKeyedSingleton<BackgroundJobDispatcher>(instanceId, (provider, _) =>
            new BackgroundJobDispatcher(
                instanceId,
                provider.GetRequiredKeyedService<ScheduliteOptions>(instanceId),
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredKeyedService<InMemoryBackgroundJobExecutionQueue>(instanceId),
                provider.GetRequiredService<ILogger<BackgroundJobDispatcher>>()
            )
        );

        // IHostedService is unkeyed because the host discovers services through this framework interface.
        services.AddSingleton<IHostedService>(provider =>
            new ScheduliteHostedService(
                instanceId,
                provider.GetRequiredKeyedService<BackgroundJobScheduler>(instanceId),
                provider.GetRequiredKeyedService<BackgroundJobDispatcher>(instanceId),
                provider.GetRequiredService<ILogger<ScheduliteHostedService>>()
            )
        );

        return services;
    }

    private static IServiceCollection AddScheduliteJobCore<TJob>(IServiceCollection services, string? instanceId, string jobId) where TJob : class, IBackgroundJob
    {
        var jobType = typeof(TJob);
        if (jobType.IsAbstract || jobType.ContainsGenericParameters)
        {
            throw new ArgumentException($"Background job type '{jobType.FullName}' must be a closed, concrete type.", nameof(TJob));
        }
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("Job ID cannot be null, empty, or whitespace.", nameof(jobId));
        }

        var registryDescriptor = services.FirstOrDefault(descriptor =>
            descriptor.ServiceType == typeof(BackgroundJobRegistry) &&
            (descriptor.IsKeyedService ? Equals(descriptor.ServiceKey, instanceId) : instanceId is null)
        );
        var registryInstance = registryDescriptor is null
            ? null
            : registryDescriptor.IsKeyedService
                ? registryDescriptor.KeyedImplementationInstance
                : registryDescriptor.ImplementationInstance;
        if (registryInstance is not BackgroundJobRegistry registry)
        {
            throw new InvalidOperationException($"Register the Schedulite runtime '{instanceId ?? "<default>"}' before registering its jobs.");
        }

        registry.Register(jobId);
        services.TryAddKeyedTransient<TJob>(instanceId);
        services.AddKeyedTransient<IBackgroundJob>((instanceId, jobId), (provider, _) => provider.GetRequiredKeyedService<TJob>(instanceId));
        return services;
    }
}
