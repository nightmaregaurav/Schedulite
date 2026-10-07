using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Schedulite;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddSchedulite<TProvider>(Action<ScheduliteOptions>? configure = null) where TProvider : class, IBackgroundJobScheduleProvider
        {
            // safeguard against null service collection
            ArgumentNullException.ThrowIfNull(services);

            // configure options
            var options = new ScheduliteOptions();
            configure?.Invoke(options);
            options.Validate();
            services.AddSingleton(options);

            // to ensure that the system clock is used consistently across the library, we register a singleton TimeProvider
            services.AddSingleton(TimeProvider.System);

            // this will send signals to the scheduler when a new job is scheduled, so that it can wake up and process the job immediately
            services.AddSingleton<SchedulerSignal>();

            // register the schedule provider that will provide the schedules for the jobs
            services.AddSingleton<IBackgroundJobScheduleProvider, TProvider>();

            // register the background job execution queue with a capacity defined in the options
            services.AddSingleton<IBackgroundJobExecutionQueue>(
                provider =>
                {
                    var configuredOptions = provider.GetRequiredService<ScheduliteOptions>();
                    return new InMemoryBackgroundJobExecutionQueue(configuredOptions.ExecutionQueueCapacity);
                }
            );

            // the registry that actually keeps track of the registered jobs and their types and allow resolving them by their ID
            services.AddSingleton<BackgroundJobRegistry>();

            // the resolver that uses the service provider to resolve job instances by their ID
            services.AddSingleton<BackgroundJobResolver>();

            // the scheduler that manages the scheduling of jobs and their execution
            services.AddSingleton<BackgroundJobScheduler>();
            // abstract layer so that external code can use instant trigger for background jobs
            services.AddSingleton<IBackgroundJobScheduler>(provider => provider.GetRequiredService<BackgroundJobScheduler>());

            // the dispatcher that actually executes the jobs from the schedules
            services.AddSingleton(
                provider =>
                {
                    var configuredOptions = provider.GetRequiredService<ScheduliteOptions>();
                    return new BackgroundJobDispatcher(
                        provider.GetRequiredService<IBackgroundJobExecutionQueue>(),
                        provider.GetRequiredService<BackgroundJobResolver>(),
                        configuredOptions.MaxConcurrency
                    );
                }
            );

            // the service that runs the scheduler and dispatcher in the background as a hosted service
            services.AddHostedService<ScheduliteHostedService>();

            return services;
        }

        public IServiceCollection AddScheduliteJob<TJob>(string jobId) where TJob : class, IBackgroundJob
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

            // register the job type as transient, so that a new instance is created for each execution
            services.TryAddTransient<TJob>();
            // register the job in the registry
            services.AddSingleton(new BackgroundJobRegistration(jobId, typeof(TJob)));

            return services;
        }
    }
}
