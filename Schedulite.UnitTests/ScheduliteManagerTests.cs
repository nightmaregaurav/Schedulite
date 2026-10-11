using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Schedulite.Abstractions;
using Schedulite.DependencyInjection;

namespace Schedulite.UnitTests;

public sealed class ScheduliteManagerTests
{
    [Fact]
    public async Task ListAsync_GroupsRegisteredJobsAndProviderSchedulesByRuntime()
    {
        var defaultProvider = CreateScheduleProvider(
            CreateSchedule("default-schedule", "default-job", "default-rule"));
        var alphaProvider = CreateScheduleProvider(
            CreateSchedule("alpha-schedule", "alpha-job", "alpha-rule"));
        var services = new ServiceCollection();
        services.AddSchedulite<TestScheduleProvider>();
        services.AddSchedulite<TestScheduleProvider>("alpha");
        services.AddKeyedSingleton<IBackgroundJobScheduleProvider>(null, defaultProvider.Object);
        services.AddKeyedSingleton<IBackgroundJobScheduleProvider>("alpha", alphaProvider.Object);
        services.AddScheduliteJob<TestJob>("default-job");
        services.AddScheduliteJob<TestJob>("alpha", "alpha-job");
        services.AddScheduliteJob<AnotherTestJob>("alpha", "alpha-manual-only-job");

        using var provider = services.BuildServiceProvider();
        var runtimes = await provider.GetRequiredService<IScheduliteManager>().ListAsync();

        var defaultRuntime = Assert.Single(runtimes, runtime => runtime.InstanceId is null);
        Assert.Contains("default-job", defaultRuntime.JobIds);
        var defaultSchedule = Assert.Single(defaultRuntime.Schedules);
        Assert.Equal("default-schedule", defaultSchedule.ScheduleId);
        Assert.True(defaultSchedule.NotQueued);
        Assert.Null(defaultSchedule.NextExecution);

        var alphaRuntime = Assert.Single(runtimes, runtime => runtime.InstanceId == "alpha");
        Assert.Contains("alpha-job", alphaRuntime.JobIds);
        Assert.Contains("alpha-manual-only-job", alphaRuntime.JobIds);
        var alphaSchedule = Assert.Single(alphaRuntime.Schedules);
        Assert.Equal("alpha-schedule", alphaSchedule.ScheduleId);
        Assert.True(alphaSchedule.NotQueued);
        Assert.Null(alphaSchedule.NextExecution);

        defaultProvider.Verify(x => x.GetNextExecution(It.IsAny<string>(), It.IsAny<DateTimeOffset>()), Times.Never);
        alphaProvider.Verify(x => x.GetNextExecution(It.IsAny<string>(), It.IsAny<DateTimeOffset>()), Times.Never);
    }

    [Fact]
    public async Task ListAsync_ReportsTheSchedulerQueuedOccurrenceByScheduleId()
    {
        var now = DateTimeOffset.UtcNow;
        var queuedOccurrence = now.AddHours(2);
        var advanceCalculated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queuedRuleCalls = 0;
        var scheduleProvider = CreateScheduleProvider(
            CreateSchedule("queued-schedule", "test-job", "queued-rule"),
            CreateSchedule("no-occurrence-schedule", "test-job", "no-occurrence-rule"));
        scheduleProvider.Setup(x => x.GetNextExecution("queued-rule", It.IsAny<DateTimeOffset>()))
            .Returns((string _, DateTimeOffset _) =>
            {
                if (Interlocked.Increment(ref queuedRuleCalls) == 1)
                {
                    return Task.FromResult<DateTimeOffset?>(now.AddSeconds(-1));
                }

                advanceCalculated.TrySetResult();
                return Task.FromResult<DateTimeOffset?>(queuedOccurrence);
            });
        scheduleProvider.Setup(x => x.GetNextExecution("no-occurrence-rule", It.IsAny<DateTimeOffset>()))
            .ReturnsAsync((DateTimeOffset?)null);

        var services = new ServiceCollection();
        services.AddSchedulite<TestScheduleProvider>();
        services.AddKeyedSingleton<IBackgroundJobScheduleProvider>(null, scheduleProvider.Object);
        services.AddScheduliteJob<TestJob>("test-job");

        using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetRequiredService<IHostedService>();
        await hostedService.StartAsync(CancellationToken.None);
        try
        {
            await advanceCalculated.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var manager = provider.GetRequiredService<IScheduliteManager>();
            BackgroundJobInfoModel? queued = null;
            var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(5);
            while (DateTimeOffset.UtcNow < timeoutAt)
            {
                var runtime = Assert.Single(await manager.ListAsync());
                queued = Assert.Single(runtime.Schedules, schedule => schedule.ScheduleId == "queued-schedule");
                if (!queued.NotQueued && queued.NextExecution == queuedOccurrence)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(10));
            }

            Assert.NotNull(queued);
            Assert.False(queued.NotQueued);
            Assert.Equal(queuedOccurrence, queued.NextExecution);

            var runtimeWithSchedules = Assert.Single(await manager.ListAsync());
            var noOccurrence = Assert.Single(runtimeWithSchedules.Schedules, schedule => schedule.ScheduleId == "no-occurrence-schedule");
            Assert.True(noOccurrence.NotQueued);
            Assert.Null(noOccurrence.NextExecution);

            scheduleProvider.Verify(x => x.GetNextExecution("queued-rule", It.IsAny<DateTimeOffset>()), Times.Exactly(2));
            scheduleProvider.Verify(x => x.GetNextExecution("no-occurrence-rule", It.IsAny<DateTimeOffset>()), Times.Once);
        }
        finally
        {
            await hostedService.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task TriggerAsync_QueuesOnTheSelectedRuntime()
    {
        var defaultJob = new RecordingTestJob();
        var betaJob = new RecordingTestJob();
        var services = new ServiceCollection();
        services.AddSchedulite<TestScheduleProvider>();
        services.AddSchedulite<TestScheduleProvider>("beta");
        services.AddKeyedSingleton<RecordingTestJob>(null, defaultJob);
        services.AddKeyedSingleton<RecordingTestJob>("beta", betaJob);
        services.AddScheduliteJob<RecordingTestJob>("shared-job");
        services.AddScheduliteJob<RecordingTestJob>("beta", "shared-job");

        using var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>().ToArray();
        foreach (var hostedService in hostedServices)
        {
            await hostedService.StartAsync(CancellationToken.None);
        }

        try
        {
            var manager = provider.GetRequiredService<IScheduliteManager>();
            await manager.TriggerAsync(null, "shared-job", "default-subject");
            await manager.TriggerAsync("beta", "shared-job", "beta-subject");

            var defaultContext = await defaultJob.Execution.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var betaContext = await betaJob.Execution.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(defaultContext.InstanceId);
            Assert.Equal("default-subject", defaultContext.SubjectId);
            Assert.Equal("beta", betaContext.InstanceId);
            Assert.Equal("beta-subject", betaContext.SubjectId);
        }
        finally
        {
            foreach (var hostedService in hostedServices.Reverse())
            {
                await hostedService.StopAsync(CancellationToken.None);
            }
        }
    }

    [Fact]
    public async Task ReloadAsync_TargetsOneRuntimeAndReloadAllAsyncTargetsEveryRuntime()
    {
        var alphaProvider = new CountingScheduleProvider();
        var betaProvider = new CountingScheduleProvider();
        var services = new ServiceCollection();
        services.AddSchedulite<TestScheduleProvider>("alpha");
        services.AddSchedulite<TestScheduleProvider>("beta");
        services.AddKeyedSingleton<IBackgroundJobScheduleProvider>("alpha", alphaProvider);
        services.AddKeyedSingleton<IBackgroundJobScheduleProvider>("beta", betaProvider);

        using var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>().ToArray();
        foreach (var hostedService in hostedServices)
        {
            await hostedService.StartAsync(CancellationToken.None);
        }

        try
        {
            await Task.WhenAll(alphaProvider.WaitForLoadCountAsync(1), betaProvider.WaitForLoadCountAsync(1))
                .WaitAsync(TimeSpan.FromSeconds(5));

            var manager = provider.GetRequiredService<IScheduliteManager>();
            await manager.ReloadAsync("alpha");
            await alphaProvider.WaitForLoadCountAsync(2).WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            Assert.Equal(1, betaProvider.LoadCount);

            await manager.ReloadAllAsync();
            await Task.WhenAll(alphaProvider.WaitForLoadCountAsync(3), betaProvider.WaitForLoadCountAsync(2))
                .WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            foreach (var hostedService in hostedServices.Reverse())
            {
                await hostedService.StopAsync(CancellationToken.None);
            }
        }
    }

    private static Mock<IBackgroundJobScheduleProvider> CreateScheduleProvider(params BackgroundJobSchedule[] schedules)
    {
        var provider = new Mock<IBackgroundJobScheduleProvider>();
        provider.Setup(x => x.GetSchedulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<BackgroundJobSchedule>)schedules);
        provider.Setup(x => x.GetNextExecution(It.IsAny<string>(), It.IsAny<DateTimeOffset>()))
            .ReturnsAsync((DateTimeOffset?)null);
        return provider;
    }

    private static BackgroundJobSchedule CreateSchedule(string scheduleId, string jobId, string rule)
    {
        return new BackgroundJobSchedule
        {
            ScheduleId = scheduleId,
            JobId = jobId,
            SubjectId = "subject",
            ScheduleRule = rule
        };
    }

    private sealed class CountingScheduleProvider : IBackgroundJobScheduleProvider
    {
        private readonly ConcurrentDictionary<int, TaskCompletionSource> _loadSignals = new();
        private int _loadCount;

        public int LoadCount => Volatile.Read(ref _loadCount);

        public Task<IReadOnlyCollection<BackgroundJobSchedule>> GetSchedulesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var loadCount = Interlocked.Increment(ref _loadCount);
            foreach (var (targetCount, signal) in _loadSignals)
            {
                if (loadCount >= targetCount)
                {
                    signal.TrySetResult();
                }
            }

            return Task.FromResult<IReadOnlyCollection<BackgroundJobSchedule>>(Array.Empty<BackgroundJobSchedule>());
        }

        public Task<DateTimeOffset?> GetNextExecution(string scheduleRule, DateTimeOffset now)
        {
            return Task.FromResult<DateTimeOffset?>(null);
        }

        public Task WaitForLoadCountAsync(int count)
        {
            if (LoadCount >= count)
            {
                return Task.CompletedTask;
            }

            var signal = _loadSignals.GetOrAdd(count, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            if (LoadCount >= count)
            {
                signal.TrySetResult();
            }

            return signal.Task;
        }
    }
}
