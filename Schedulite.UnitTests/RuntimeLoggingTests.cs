using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Schedulite.Abstractions;
using Schedulite.DependencyInjection;

namespace Schedulite.UnitTests;

public sealed class RuntimeLoggingTests
{
    [Fact]
    public async Task Runtime_LogsLifecycleTriggersAndUnknownJobsAtUsefulLevels()
    {
        using var logs = new CapturingLoggerProvider();
        var schedules = new Mock<IBackgroundJobScheduleProvider>();
        schedules.Setup(provider => provider.GetSchedulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<BackgroundJobSchedule>());
        var job = new RecordingTestJob();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
        services.AddSchedulite<TestScheduleProvider>();
        services.AddKeyedSingleton<IBackgroundJobScheduleProvider>(null, schedules.Object);
        services.AddKeyedSingleton<RecordingTestJob>(null, job);
        services.AddScheduliteJob<RecordingTestJob>("recording");

        using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetRequiredService<IHostedService>();
        await hostedService.StartAsync(CancellationToken.None);

        try
        {
            var scheduler = provider.GetRequiredKeyedService<IBackgroundJobScheduler>(null);
            await logs.WaitForAsync(entry => entry.Level == LogLevel.Information && entry.Message.Contains("Reloaded schedules", StringComparison.Ordinal));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => scheduler.TriggerAsync("missing", "subject"));
            await logs.WaitForAsync(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("unregistered job", StringComparison.Ordinal));

            var executionId = await scheduler.TriggerAsync("recording", "subject-42");
            await job.Execution.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await logs.WaitForAsync(entry => entry.Level == LogLevel.Information && entry.Message.Contains("Completed execution", StringComparison.Ordinal));

            Assert.Contains(logs.Entries, entry => entry.Level == LogLevel.Trace && entry.Message.Contains("No scheduled executions", StringComparison.Ordinal));
            Assert.Contains(logs.Entries, entry => entry.Level == LogLevel.Debug && entry.Message.Contains("Queued Manual execution", StringComparison.Ordinal));
            Assert.Contains(logs.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains(executionId.ToString(), StringComparison.Ordinal));
        }
        finally
        {
            await hostedService.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Dispatcher_LogsJobExceptionsAndKeepsProcessingExecutions()
    {
        using var logs = new CapturingLoggerProvider();
        var job = new FailOnceJob();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
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
            var failedLog = await logs.WaitForAsync(entry => entry.Level == LogLevel.Error && entry.Message.Contains("failed", StringComparison.Ordinal));

            Assert.NotNull(failedLog.Exception);

            await scheduler.TriggerAsync("fail-once", null);
            await job.SecondAttempt.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await logs.WaitForAsync(entry => entry.Level == LogLevel.Information && entry.Message.Contains("Completed execution", StringComparison.Ordinal));
        }
        finally
        {
            await hostedService.StopAsync(CancellationToken.None);
        }
    }
}

internal sealed record CapturedLog(string Category, LogLevel Level, string Message, Exception? Exception);

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLog> _entries = new();
    private readonly SemaphoreSlim _changed = new(0);

    public CapturedLog[] Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName)
    {
        return new CapturingLogger(categoryName, this);
    }

    public async Task<CapturedLog> WaitForAsync(Func<CapturedLog, bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var match = _entries.FirstOrDefault(predicate);
            if (match is not null)
            {
                return match;
            }

            await _changed.WaitAsync(timeout.Token);
        }
    }

    internal void Add(CapturedLog entry)
    {
        _entries.Enqueue(entry);
        _changed.Release();
    }

    public void Dispose()
    {
        _changed.Dispose();
    }

    private sealed class CapturingLogger(string categoryName, CapturingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            owner.Add(new CapturedLog(categoryName, logLevel, formatter(state, exception), exception));
        }
    }
}
