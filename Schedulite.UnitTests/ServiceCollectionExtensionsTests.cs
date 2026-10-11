using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Schedulite.Abstractions;
using Schedulite.Configuration;
using Schedulite.DependencyInjection;

namespace Schedulite.UnitTests;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSchedulite_RegistersRuntimeAndProvider()
    {
        var services = new ServiceCollection();

        var schedules = new Mock<IBackgroundJobScheduleProvider>();
        schedules.Setup(x => x.GetSchedulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<BackgroundJobSchedule>());
        schedules.Setup(x => x.GetNextExecution(It.IsAny<string>(), It.IsAny<DateTimeOffset>()))
            .ReturnsAsync((DateTimeOffset?)null);
        services.AddSchedulite<TestScheduleProvider>(new ScheduliteOptions
        {
            MaxConcurrency = 2,
            ExecutionQueueCapacity = 8
        });
        services.AddKeyedSingleton<IBackgroundJobScheduleProvider>(null, schedules.Object);
        services.AddScheduliteJob<TestJob>("test");

        using var provider = services.BuildServiceProvider();
        Assert.Same(schedules.Object, provider.GetRequiredKeyedService<IBackgroundJobScheduleProvider>(null));
        Assert.IsAssignableFrom<IBackgroundJobScheduler>(provider.GetRequiredKeyedService<IBackgroundJobScheduler>(null));
        Assert.Equal(2, provider.GetRequiredKeyedService<ScheduliteOptions>(null).MaxConcurrency);
        Assert.Equal(8, provider.GetRequiredKeyedService<ScheduliteOptions>(null).ExecutionQueueCapacity);
    }

    [Theory]
    [InlineData(0, 10, "MaxConcurrency")]
    [InlineData(2, 0, "ExecutionQueueCapacity")]
    public void AddSchedulite_RejectsInvalidOptions(int concurrency, int capacity, string parameter)
    {
        var services = new ServiceCollection();
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            services.AddSchedulite<TestScheduleProvider>(new ScheduliteOptions
            {
                MaxConcurrency = concurrency,
                ExecutionQueueCapacity = capacity
            }));

        Assert.Equal(parameter, exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void AddScheduliteJob_RejectsMissingId(string? jobId)
    {
        var services = new ServiceCollection();
        Assert.ThrowsAny<ArgumentException>(() => services.AddScheduliteJob<TestJob>(jobId!));
    }

    [Fact]
    public void AddScheduliteJob_RejectsDuplicateIdsDuringRegistration()
    {
        var services = new ServiceCollection();
        services.AddSchedulite<TestScheduleProvider>();
        services.AddScheduliteJob<TestJob>("same");
        Assert.Throws<ArgumentException>(() => services.AddScheduliteJob<AnotherTestJob>("same"));
    }

    [Fact]
    public async Task TriggerAsync_RejectsUnknownJob()
    {
        var services = new ServiceCollection();
        services.AddSchedulite<TestScheduleProvider>();
        using var provider = services.BuildServiceProvider();

        var scheduler = provider.GetRequiredKeyedService<IBackgroundJobScheduler>(null);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => scheduler.TriggerAsync("missing", "subject"));
    }

    [Fact]
    public async Task TriggerAsync_QueuesJobAndProvidesManualExecutionContext()
    {
        var job = new RecordingTestJob();
        var scheduleProvider = new Mock<IBackgroundJobScheduleProvider>();
        scheduleProvider.Setup(x => x.GetSchedulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<BackgroundJobSchedule>());
        var services = new ServiceCollection();
        services.AddSchedulite<TestScheduleProvider>();
        services.AddKeyedSingleton<IBackgroundJobScheduleProvider>(null, scheduleProvider.Object);
        services.AddKeyedSingleton<RecordingTestJob>(null, job);
        services.AddScheduliteJob<RecordingTestJob>("recording");

        using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetRequiredService<IHostedService>();
        await hostedService.StartAsync(CancellationToken.None);
        try
        {
            await provider.GetRequiredKeyedService<IBackgroundJobScheduler>(null).TriggerAsync("recording", "subject-42");
            var context = await job.Execution.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal("recording", context.JobId);
            Assert.Null(context.InstanceId);
            Assert.Equal("subject-42", context.SubjectId);
            Assert.Null(context.ScheduleId);
            Assert.Equal(BackgroundJobTrigger.Manual, context.Trigger);
            Assert.NotEqual(Guid.Empty, context.ExecutionId);
        }
        finally
        {
            await hostedService.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task TriggerAsync_PropagatesCancellation()
    {
        var services = new ServiceCollection();
        services.AddSchedulite<TestScheduleProvider>();
        services.AddScheduliteJob<TestJob>("test");
        using var provider = services.BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.GetRequiredKeyedService<IBackgroundJobScheduler>(null).TriggerAsync("test", "subject", cancellation.Token));
    }

    [Theory]
    [InlineData(null, "subject")]
    [InlineData(" ", "subject")]
    [InlineData("test", " ")]
    public async Task TriggerAsync_RejectsBlankJobOrSubjectIds(string? jobId, string? subjectId)
    {
        var services = new ServiceCollection();
        services.AddSchedulite<TestScheduleProvider>();
        services.AddScheduliteJob<TestJob>("test");
        using var provider = services.BuildServiceProvider();

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            provider.GetRequiredKeyedService<IBackgroundJobScheduler>(null).TriggerAsync(jobId!, subjectId!));
    }

    [Fact]
    public async Task Scheduler_EnqueuesDueScheduleWithScheduledContextAndScheduleId()
    {
        var job = new RecordingTestJob();
        var now = DateTimeOffset.UtcNow;
        var schedules = new Mock<IBackgroundJobScheduleProvider>();
        schedules.Setup(x => x.GetSchedulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new BackgroundJobSchedule
                {
                    ScheduleId = "schedule-1",
                    JobId = "recording",
                    SubjectId = "scheduled-subject",
                    ScheduleRule = "test-rule"
                }
            });
        schedules.SetupSequence(x => x.GetNextExecution("test-rule", It.IsAny<DateTimeOffset>()))
            .ReturnsAsync(now.AddSeconds(-1))
            .ReturnsAsync(now.AddHours(1));

        var services = new ServiceCollection();
        services.AddSchedulite<TestScheduleProvider>();
        services.AddKeyedSingleton<IBackgroundJobScheduleProvider>(null, schedules.Object);
        services.AddKeyedSingleton<RecordingTestJob>(null, job);
        services.AddScheduliteJob<RecordingTestJob>("recording");
        using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetRequiredService<IHostedService>();
        await hostedService.StartAsync(CancellationToken.None);
        try
        {
            var context = await job.Execution.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal("recording", context.JobId);
            Assert.Null(context.InstanceId);
            Assert.Equal("scheduled-subject", context.SubjectId);
            Assert.Equal("schedule-1", context.ScheduleId);
            Assert.Equal(BackgroundJobTrigger.Scheduled, context.Trigger);
        }
        finally
        {
            await hostedService.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task NamedRuntimes_IsolateSchedulersAndIncludeTheirIdsInExecutionContext()
    {
        var recorder = new ExecutionContextRecorder();
        var services = new ServiceCollection();
        services.AddSchedulite<TestScheduleProvider>("alpha", new ScheduliteOptions
        {
            MaxConcurrency = 1
        });
        services.AddSchedulite<TestScheduleProvider>("beta", new ScheduliteOptions
        {
            MaxConcurrency = 3
        });
        services.AddScheduliteJob<AlphaInstanceJob>("alpha", "same-job-id");
        services.AddScheduliteJob<BetaInstanceJob>("beta", "same-job-id");
        services.AddSingleton(recorder);

        using var provider = services.BuildServiceProvider();
        Assert.Equal(1, provider.GetRequiredKeyedService<ScheduliteOptions>("alpha").MaxConcurrency);
        Assert.Equal(3, provider.GetRequiredKeyedService<ScheduliteOptions>("beta").MaxConcurrency);
        var hostedServices = provider.GetServices<IHostedService>().ToArray();
        foreach (var hostedService in hostedServices)
        {
            await hostedService.StartAsync(CancellationToken.None);
        }

        try
        {
            var alpha = provider.GetRequiredKeyedService<IBackgroundJobScheduler>("alpha");
            var beta = provider.GetRequiredKeyedService<IBackgroundJobScheduler>("beta");
            await alpha.TriggerAsync("same-job-id", "subject");
            await beta.TriggerAsync("same-job-id", "subject");

            var contexts = await recorder.TwoExecutions.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(new[] { "alpha", "beta" }, contexts.Select(context => context.InstanceId).Order().ToArray());
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
    public async Task ScheduleProvider_CanUseScopedAsyncDisposableDependencies()
    {
        var services = new ServiceCollection();
        services.AddSingleton<DisposalTracker>();
        services.AddScoped<ScopedDependency>();
        services.AddSchedulite<ScopedDependencyScheduleProvider>("scoped");

        using var provider = services.BuildServiceProvider(validateScopes: true);
        var hostedService = provider.GetRequiredService<IHostedService>();
        await hostedService.StartAsync(CancellationToken.None);

        try
        {
            var tracker = provider.GetRequiredService<DisposalTracker>();
            await tracker.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, tracker.DisposeCount);
        }
        finally
        {
            await hostedService.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Dispatcher_ContinuesAfterAJobThrows()
    {
        var job = new FailOnceJob();
        var services = new ServiceCollection();
        services.AddSchedulite<TestScheduleProvider>();
        services.AddKeyedSingleton<FailOnceJob>(null, job);
        services.AddScheduliteJob<FailOnceJob>("fail-once");

        using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetRequiredService<IHostedService>();
        await hostedService.StartAsync(CancellationToken.None);
        try
        {
            var scheduler = provider.GetRequiredKeyedService<IBackgroundJobScheduler>(null);
            await scheduler.TriggerAsync("fail-once", null);
            await job.FirstAttempt.Task.WaitAsync(TimeSpan.FromSeconds(5));

            await scheduler.TriggerAsync("fail-once", null);
            var successfulExecution = await job.SecondAttempt.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal("fail-once", successfulExecution.JobId);
        }
        finally
        {
            await hostedService.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Dispatcher_PerJobScopeSerializesExecutionsAndReleasesTheNextOne()
    {
        var job = new SerializedJob();
        var services = new ServiceCollection();
        services.AddSchedulite<TestScheduleProvider>();
        services.AddKeyedSingleton<SerializedJob>(null, job);
        services.AddScheduliteJob<SerializedJob>("serialized");

        using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetRequiredService<IHostedService>();
        await hostedService.StartAsync(CancellationToken.None);
        try
        {
            var scheduler = provider.GetRequiredKeyedService<IBackgroundJobScheduler>(null);
            await scheduler.TriggerAsync("serialized", "subject");
            await job.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await scheduler.TriggerAsync("serialized", "subject");

            await Task.Delay(TimeSpan.FromMilliseconds(100));
            Assert.False(job.SecondStarted.Task.IsCompleted);

            job.ReleaseFirst.TrySetResult();
            await job.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            job.ReleaseFirst.TrySetResult();
            await hostedService.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ReloadSchedulesAsync_RecalculatesWhenScheduleRuleChanges()
    {
        var job = new RecordingTestJob();
        var initialLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loadCount = 0;
        var schedules = new Mock<IBackgroundJobScheduleProvider>();
        schedules.Setup(x => x.GetSchedulesAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                var isInitialLoad = Interlocked.Increment(ref loadCount) == 1;
                if (isInitialLoad)
                {
                    initialLoad.TrySetResult();
                }

                var rule = isInitialLoad ? "old-rule" : "new-rule";
                IReadOnlyCollection<BackgroundJobSchedule> currentSchedules =
                [
                    new()
                    {
                        ScheduleId = "stable-id",
                        JobId = "recording",
                        SubjectId = "subject",
                        ScheduleRule = rule
                    }
                ];
                return Task.FromResult(currentSchedules);
            });
        schedules.SetupSequence(x => x.GetNextExecution("old-rule", It.IsAny<DateTimeOffset>()))
            .ReturnsAsync(DateTimeOffset.UtcNow.AddHours(1));
        schedules.SetupSequence(x => x.GetNextExecution("new-rule", It.IsAny<DateTimeOffset>()))
            .ReturnsAsync(DateTimeOffset.UtcNow.AddSeconds(-1))
            .ReturnsAsync(DateTimeOffset.UtcNow.AddHours(1));

        var services = new ServiceCollection();
        services.AddSchedulite<TestScheduleProvider>();
        services.AddKeyedSingleton<IBackgroundJobScheduleProvider>(null, schedules.Object);
        services.AddKeyedSingleton<RecordingTestJob>(null, job);
        services.AddScheduliteJob<RecordingTestJob>("recording");
        using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetRequiredService<IHostedService>();
        await hostedService.StartAsync(CancellationToken.None);

        try
        {
            await initialLoad.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await provider.GetRequiredKeyedService<IBackgroundJobScheduler>(null).ReloadSchedulesAsync();
            var context = await job.Execution.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal("stable-id", context.ScheduleId);
            schedules.Verify(x => x.GetNextExecution("new-rule", It.IsAny<DateTimeOffset>()), Times.AtLeastOnce);
        }
        finally
        {
            await hostedService.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Scheduler_RemovingAnExhaustedScheduleDoesNotFaultTheHostedService()
    {
        var job = new RecordingTestJob();
        var advanceStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowAdvance = new TaskCompletionSource<DateTimeOffset?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var schedules = new Mock<IBackgroundJobScheduleProvider>();
        schedules.Setup(x => x.GetSchedulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new BackgroundJobSchedule
                {
                    ScheduleId = "one-shot",
                    JobId = "recording",
                    SubjectId = "subject",
                    ScheduleRule = "one-shot-rule"
                }
            });
        schedules.SetupSequence(x => x.GetNextExecution("one-shot-rule", It.IsAny<DateTimeOffset>()))
            .ReturnsAsync(DateTimeOffset.UtcNow.AddSeconds(-1))
            .Returns(() =>
            {
                advanceStarted.TrySetResult();
                return allowAdvance.Task;
            });

        var services = new ServiceCollection();
        services.AddSchedulite<TestScheduleProvider>();
        services.AddKeyedSingleton<IBackgroundJobScheduleProvider>(null, schedules.Object);
        services.AddKeyedSingleton<RecordingTestJob>(null, job);
        services.AddScheduliteJob<RecordingTestJob>("recording");
        using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetRequiredService<IHostedService>();
        await hostedService.StartAsync(CancellationToken.None);

        try
        {
            await advanceStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await job.Execution.Task.WaitAsync(TimeSpan.FromSeconds(5));
            allowAdvance.SetResult(null);

            var runtimeTask = Assert.IsAssignableFrom<BackgroundService>(hostedService).ExecuteTask!;
            await Task.WhenAny(runtimeTask, Task.Delay(TimeSpan.FromMilliseconds(100)));
            Assert.False(runtimeTask.IsCompleted);
        }
        finally
        {
            await hostedService.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Scheduler_BoundsCatchUpForSchedulesWithManyMissedOccurrences()
    {
        var job = new RecordingTestJob();
        var anchor = DateTimeOffset.UtcNow;
        var fastForwardObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextExecutionCalls = 0;
        var schedules = new Mock<IBackgroundJobScheduleProvider>();
        schedules.Setup(x => x.GetSchedulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new BackgroundJobSchedule
                {
                    ScheduleId = "frequent",
                    JobId = "recording",
                    SubjectId = "subject",
                    ScheduleRule = "frequent-rule"
                }
            });
        schedules.Setup(x => x.GetNextExecution("frequent-rule", It.IsAny<DateTimeOffset>()))
            .Returns((string _, DateTimeOffset after) =>
            {
                if (Interlocked.Increment(ref nextExecutionCalls) == 1)
                {
                    return Task.FromResult<DateTimeOffset?>(anchor.AddDays(-2));
                }

                if (after < anchor.AddDays(-1))
                {
                    return Task.FromResult<DateTimeOffset?>(after.AddMilliseconds(1));
                }

                fastForwardObserved.TrySetResult();
                return Task.FromResult<DateTimeOffset?>(after.AddSeconds(1));
            });

        var services = new ServiceCollection();
        services.AddSchedulite<TestScheduleProvider>();
        services.AddKeyedSingleton<IBackgroundJobScheduleProvider>(null, schedules.Object);
        services.AddKeyedSingleton<RecordingTestJob>(null, job);
        services.AddScheduliteJob<RecordingTestJob>("recording");
        using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetRequiredService<IHostedService>();
        await hostedService.StartAsync(CancellationToken.None);

        try
        {
            await job.Execution.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await fastForwardObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.InRange(nextExecutionCalls, 1_000, 1_005);
        }
        finally
        {
            await hostedService.StopAsync(CancellationToken.None);
        }
    }
}

public sealed class TestScheduleProvider : IBackgroundJobScheduleProvider
{
    public Task<IReadOnlyCollection<BackgroundJobSchedule>> GetSchedulesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyCollection<BackgroundJobSchedule>>(Array.Empty<BackgroundJobSchedule>());
    }

    public Task<DateTimeOffset?> GetNextExecution(string scheduleRule, DateTimeOffset now)
    {
        return Task.FromResult<DateTimeOffset?>(null);
    }
}

public class TestJob : IBackgroundJob
{
    public string JobName => "Test";
    public string JobDescription => "Test job";
    public Task ExecuteAsync(BackgroundJobContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

public sealed class AnotherTestJob : TestJob { }

public sealed class RecordingTestJob : IBackgroundJob
{
    public TaskCompletionSource<BackgroundJobContext> Execution { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string JobName => "Recording";
    public string JobDescription => "Captures the execution context for integration tests.";
    public Task ExecuteAsync(BackgroundJobContext context, CancellationToken cancellationToken)
    {
        Execution.TrySetResult(context);
        return Task.CompletedTask;
    }
}

public sealed class ExecutionContextRecorder
{
    private readonly ConcurrentBag<BackgroundJobContext> _contexts = [];
    private int _executionCount;

    public TaskCompletionSource<BackgroundJobContext[]> TwoExecutions { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Record(BackgroundJobContext context)
    {
        _contexts.Add(context);
        if (Interlocked.Increment(ref _executionCount) == 2)
        {
            TwoExecutions.TrySetResult(_contexts.ToArray());
        }
    }
}

public sealed class AlphaInstanceJob(ExecutionContextRecorder recorder) : IBackgroundJob
{
    public string JobName => "Alpha instance";
    public string JobDescription => "Records executions for the alpha runtime.";
    public Task ExecuteAsync(BackgroundJobContext context, CancellationToken cancellationToken)
    {
        recorder.Record(context);
        return Task.CompletedTask;
    }
}

public sealed class BetaInstanceJob(ExecutionContextRecorder recorder) : IBackgroundJob
{
    public string JobName => "Beta instance";
    public string JobDescription => "Records executions for the beta runtime.";
    public Task ExecuteAsync(BackgroundJobContext context, CancellationToken cancellationToken)
    {
        recorder.Record(context);
        return Task.CompletedTask;
    }
}

public sealed class ScopedDependency : IAsyncDisposable
{
    private readonly DisposalTracker _tracker;
    public bool IsAvailable => true;

    public ScopedDependency(DisposalTracker tracker) => _tracker = tracker;

    public ValueTask DisposeAsync()
    {
        _tracker.DisposeCount++;
        _tracker.Disposed.TrySetResult();
        return ValueTask.CompletedTask;
    }
}

public sealed class DisposalTracker
{
    public int DisposeCount {get; set; }
    public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class ScopedDependencyScheduleProvider(ScopedDependency dependency) : IBackgroundJobScheduleProvider
{
    public Task<IReadOnlyCollection<BackgroundJobSchedule>> GetSchedulesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyCollection<BackgroundJobSchedule>>(
            dependency.IsAvailable ? Array.Empty<BackgroundJobSchedule>() : throw new InvalidOperationException());
    }

    public Task<DateTimeOffset?> GetNextExecution(string scheduleRule, DateTimeOffset now)
    {
        return Task.FromResult<DateTimeOffset?>(null);
    }
}

public sealed class FailOnceJob : IBackgroundJob
{
    private int _attemptCount;

    public TaskCompletionSource FirstAttempt { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<BackgroundJobContext> SecondAttempt { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string JobName => "Fail once";
    public string JobDescription => "Throws once to verify dispatcher fault isolation.";

    public Task ExecuteAsync(BackgroundJobContext context, CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _attemptCount) == 1)
        {
            FirstAttempt.TrySetResult();
            throw new InvalidOperationException("Expected test failure.");
        }

        SecondAttempt.TrySetResult(context);
        return Task.CompletedTask;
    }
}

public sealed class SerializedJob : IBackgroundJob
{
    private int _executionCount;

    public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource SecondStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string JobName => "Serialized";
    public string JobDescription => "Verifies per-job execution serialization.";
    public ExecutionConcurrencyScope ConcurrencyScope => ExecutionConcurrencyScope.PerJob;

    public async Task ExecuteAsync(BackgroundJobContext context, CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _executionCount) == 1)
        {
            FirstStarted.TrySetResult();
            await ReleaseFirst.Task.WaitAsync(cancellationToken);
            return;
        }

        SecondStarted.TrySetResult();
    }
}
