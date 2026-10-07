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
        services.AddSchedulite<TestScheduleProvider>(options =>
        {
            options.MaxConcurrency = 2;
            options.ExecutionQueueCapacity = 8;
        });
        services.AddSingleton(schedules.Object);
        services.AddScheduliteJob<TestJob>("test");

        using var provider = services.BuildServiceProvider();
        Assert.Same(schedules.Object, provider.GetRequiredService<IBackgroundJobScheduleProvider>());
        Assert.IsAssignableFrom<IBackgroundJobScheduler>(provider.GetRequiredService<IBackgroundJobScheduler>());
        Assert.Equal(2, provider.GetRequiredService<ScheduliteOptions>().MaxConcurrency);
        Assert.Equal(8, provider.GetRequiredService<ScheduliteOptions>().ExecutionQueueCapacity);
    }

    [Theory]
    [InlineData(0, 10, "MaxConcurrency")]
    [InlineData(2, 0, "ExecutionQueueCapacity")]
    public void AddSchedulite_RejectsInvalidOptions(int concurrency, int capacity, string parameter)
    {
        var services = new ServiceCollection();
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            services.AddSchedulite<TestScheduleProvider>(options =>
            {
                options.MaxConcurrency = concurrency;
                options.ExecutionQueueCapacity = capacity;
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
    public void AddScheduliteJob_RejectsDuplicateIdsWhenRegistryIsResolved()
    {
        var services = new ServiceCollection();
        services.AddSchedulite<TestScheduleProvider>();
        services.AddScheduliteJob<TestJob>("same");
        services.AddScheduliteJob<AnotherTestJob>("same");
        using var provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() =>
        {
            _ = provider.GetRequiredService<IBackgroundJobScheduler>();
        });
    }

    [Fact]
    public async Task TriggerAsync_RejectsUnknownJob()
    {
        var services = new ServiceCollection();
        services.AddSchedulite<TestScheduleProvider>();
        using var provider = services.BuildServiceProvider();

        var scheduler = provider.GetRequiredService<IBackgroundJobScheduler>();
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
        services.AddSingleton(scheduleProvider.Object);
        services.AddSingleton(job);
        services.AddScheduliteJob<RecordingTestJob>("recording");

        using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetRequiredService<IHostedService>();
        await hostedService.StartAsync(CancellationToken.None);
        try
        {
            await provider.GetRequiredService<IBackgroundJobScheduler>().TriggerAsync("recording", "subject-42");
            var context = await job.Execution.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal("recording", context.JobId);
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
            provider.GetRequiredService<IBackgroundJobScheduler>().TriggerAsync("test", "subject", cancellation.Token));
    }

    [Theory]
    [InlineData(null, "subject")]
    [InlineData(" ", "subject")]
    [InlineData("test", null)]
    [InlineData("test", " ")]
    public async Task TriggerAsync_RejectsBlankJobOrSubjectIds(string? jobId, string? subjectId)
    {
        var services = new ServiceCollection();
        services.AddSchedulite<TestScheduleProvider>();
        services.AddScheduliteJob<TestJob>("test");
        using var provider = services.BuildServiceProvider();

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            provider.GetRequiredService<IBackgroundJobScheduler>().TriggerAsync(jobId!, subjectId!));
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
        services.AddSingleton(schedules.Object);
        services.AddSingleton(job);
        services.AddScheduliteJob<RecordingTestJob>("recording");
        using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetRequiredService<IHostedService>();
        await hostedService.StartAsync(CancellationToken.None);
        try
        {
            var context = await job.Execution.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal("recording", context.JobId);
            Assert.Equal("scheduled-subject", context.SubjectId);
            Assert.Equal("schedule-1", context.ScheduleId);
            Assert.Equal(BackgroundJobTrigger.Scheduled, context.Trigger);
        }
        finally
        {
            await hostedService.StopAsync(CancellationToken.None);
        }
    }
}

public sealed class TestScheduleProvider : IBackgroundJobScheduleProvider
{
    public Task<IReadOnlyCollection<BackgroundJobSchedule>> GetSchedulesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<BackgroundJobSchedule>>(Array.Empty<BackgroundJobSchedule>());

    public Task<DateTimeOffset?> GetNextExecution(string scheduleRule, DateTimeOffset now) =>
        Task.FromResult<DateTimeOffset?>(null);
}

public class TestJob : IBackgroundJob
{
    public string JobName => "Test";
    public string JobDescription => "Test job";
    public Task ExecuteAsync(BackgroundJobContext context, CancellationToken cancellationToken) => Task.CompletedTask;
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
