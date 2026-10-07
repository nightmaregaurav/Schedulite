# Schedulite

Schedulite is a small, extensible background-job scheduler for .NET applications. It runs registered jobs in a hosted service, supports recurring schedules supplied by your application, and exposes an API for triggering a job immediately.

The scheduler does not impose a storage provider or a cron format. Your `IBackgroundJobScheduleProvider` loads schedules and translates each application-defined `ScheduleRule` into its next execution time. This makes it suitable for schedules stored in a database, configuration service, or another application-specific system.

## Requirements

- .NET 10
- An application using `Microsoft.Extensions.Hosting`

## Install

```bash
dotnet add package nightmaregaurav.schedulite
```

## Quick start

Register the scheduler and one or more jobs in `Program.cs`:

```csharp
using Schedulite.DependencyInjection;

builder.Services.AddSchedulite<MyScheduleProvider>(options =>
{
    options.MaxConcurrency = 4;
    options.ExecutionQueueCapacity = 100;
});

builder.Services.AddScheduliteJob<SendReminderJob>("send-reminder");
```

Implement a job with `IBackgroundJob`:

```csharp
using Schedulite.Abstractions;

public sealed class SendReminderJob : IBackgroundJob
{
    public string JobName => "Send reminder";
    public string JobDescription => "Sends a reminder for a subject.";

    public Task ExecuteAsync(
        BackgroundJobContext context,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Sending a reminder for {context.SubjectId}.");
        return Task.CompletedTask;
    }
}
```

Implement the schedule provider. Schedule IDs must be stable and unique; do not generate a new ID every time schedules are loaded.

```csharp
using Schedulite.Abstractions;

public sealed class MyScheduleProvider : IBackgroundJobScheduleProvider
{
    public Task<IReadOnlyCollection<BackgroundJobSchedule>> GetSchedulesAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<BackgroundJobSchedule> schedules =
        [
            new()
            {
                ScheduleId = "daily-reminder",
                JobId = "send-reminder",
                SubjectId = "customer-42",
                ScheduleRule = "daily",
                Enabled = true
            }
        ];

        return Task.FromResult(schedules);
    }

    public Task<DateTimeOffset?> GetNextExecution(
        string scheduleRule,
        DateTimeOffset now)
    {
        return scheduleRule switch
        {
            "daily" => Task.FromResult<DateTimeOffset?>(now.AddDays(1)),
            _ => throw new ArgumentException(
                $"Unknown schedule rule '{scheduleRule}'.",
                nameof(scheduleRule))
        };
    }
}
```

The schedule provider is reloaded when the hosted scheduler starts and when it is signalled by the scheduler runtime. A schedule that returns `null` from `GetNextExecution` is not scheduled. Disabled schedules are ignored.

## Trigger a job immediately

Inject `IBackgroundJobScheduler` and call `TriggerAsync`:

```csharp
using Schedulite.Abstractions;

public sealed class ReminderController(IBackgroundJobScheduler scheduler)
{
    public async Task<Guid> TriggerAsync(
        string subjectId,
        CancellationToken cancellationToken)
    {
        return await scheduler.TriggerAsync(
            "send-reminder",
            subjectId,
            cancellationToken);
    }
}
```

Each execution receives a `BackgroundJobContext` containing an execution ID, job ID, subject ID, optional schedule ID, and whether the trigger was manual or scheduled. Jobs should honor the cancellation token.

## Configuration

`ScheduliteOptions` provides:

- `MaxConcurrency`: maximum number of jobs dispatched at once. Defaults to `4`.
- `ExecutionQueueCapacity`: maximum number of queued execution requests. Defaults to `100`.

Both values must be greater than zero.

## Example application

The [`Schedulite.Example`](https://github.com/nightmaregaurav/Schedulite/tree/main/Schedulite.Example) project is a runnable ASP.NET Core application showing dependency injection, multiple jobs, schedule-provider rules, manual triggering, and an API endpoint for inspecting schedules. It is the best starting point for a complete integration:

```bash
dotnet run --project Schedulite.Example
```

The example uses deterministic schedule IDs and demonstrates application-owned schedule rules such as `every-minute` and `every-2-minute`.

## License

Schedulite is available under the [MIT License](LICENSE).
