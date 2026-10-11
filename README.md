# Schedulite

Schedulite is a small, extensible background-job scheduler for .NET applications. It runs registered jobs in a hosted service, supports recurring schedules supplied by your application, and exposes APIs for triggering jobs immediately and requesting a schedule reload.

The scheduler does not impose a storage provider or a rule format. A `ScheduleRule` can be any string your `IBackgroundJobScheduleProvider` understands, including cron syntax or an identifier parsed by a lexer or other parser you provide. Your provider loads schedules and translates each rule into its next execution time, so rules can come from a database, configuration service, or another application-specific system.

For more predictable wall-clock timing, prefer rules anchored to a specific time, such as “every day at 9 p.m.” or “at the top of the next hour.” These rules can return the same scheduled clock time whenever they are evaluated, regardless of the current time offset. Small delays can still occur while work waits in the execution queue. Relative interval rules, such as “every five minutes,” depend more on when the schedule is evaluated and can drift. Schedulite calculates later cycles from the schedule's previous next-execution time to help limit that drift, but clock-based rules are recommended when execution time should stay aligned to a particular time of day.

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

## Multiple Schedulite runtimes

An application can register multiple isolated runtimes. Each runtime has its own schedule provider, registered job IDs, options, in-memory queue, concurrency limit, and hosted service. Job and schedule IDs are case-sensitive and scoped to their runtime, so the same job ID can be used by different runtimes.

```csharp
builder.Services.AddSchedulite<BillingScheduleProvider>("billing", options =>
{
    options.MaxConcurrency = 2;
});
builder.Services.AddScheduliteJob<InvoiceJob>("billing", "process");

builder.Services.AddSchedulite<NotificationScheduleProvider>("notifications");
builder.Services.AddScheduliteJob<EmailJob>("notifications", "process");
```

Schedulite registers its scheduler, schedule provider, options, and jobs as keyed services. The no-ID registration methods use the null key. Inject the default runtime with `[FromKeyedServices(null)]`; use the runtime ID to inject a named runtime.

For a named runtime, inject the scheduler with its key:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Schedulite.Abstractions;

public sealed class BillingController(
    [FromKeyedServices("billing")] IBackgroundJobScheduler scheduler)
{
    public Task<Guid> TriggerAsync(CancellationToken cancellationToken) =>
        scheduler.TriggerAsync("process", "invoice-42", cancellationToken);
}
```

`AddSchedulite<TProvider>(...)` and `AddScheduliteJob<TJob>(jobId)` without an instance ID register the default runtime under the null key. Named runtimes use their ID as the key. Each execution's `BackgroundJobContext.InstanceId` identifies the runtime that produced it and is `null` for the default runtime.

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

Jobs may choose how their executions are serialized. The default is `Unrestricted`, which allows executions to overlap. Set `ConcurrencyScope` to `PerJob` to allow only one execution of that registered job at a time, or `PerSubject` to allow one execution per job and subject pair while still running different subjects concurrently:

```csharp
public sealed class ProcessTenantQueueJob : IBackgroundJob
{
    public string JobName => "Process tenant queue";
    public string JobDescription => "Processes pending work for a tenant.";
    public ExecutionConcurrencyScope ConcurrencyScope => ExecutionConcurrencyScope.PerSubject;

    public Task ExecuteAsync(
        BackgroundJobContext context,
        CancellationToken cancellationToken)
    {
        // Process pending work for context.SubjectId.
        return Task.CompletedTask;
    }
}
```

When an execution conflicts with one already running, it waits for that execution to finish. A `null` subject is treated as one shared subject for that job, and may be supplied to `TriggerAsync` for jobs without a subject. This serialization applies within one Schedulite process; jobs that run in multiple application instances still need application-level coordination.

Executions waiting on a concurrency lock occupy dispatcher slots. A large backlog for one serialized job can therefore delay unrelated jobs in the same runtime.

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
                ScheduleRule = "daily-at-9pm",
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
            "daily-at-9pm" => Task.FromResult<DateTimeOffset?>(
                now.TimeOfDay < TimeSpan.FromHours(21)
                    ? now.Date.AddHours(21)
                    : now.Date.AddDays(1).AddHours(21)),
            _ => throw new ArgumentException(
                $"Unknown schedule rule '{scheduleRule}'.",
                nameof(scheduleRule))
        };
    }
}
```

The schedule provider is reloaded when the hosted scheduler starts and when it is signalled by the scheduler runtime. A schedule that returns `null` from `GetNextExecution` is not scheduled. Disabled schedules are ignored.

Schedulite resolves the provider in an asynchronous dependency injection scope for each provider call. This lets providers use scoped services such as database contexts; provider instance fields should not be used to carry state from one call to another. `GetNextExecution` has no cancellation token, so keep that calculation bounded. The scheduler retains every enabled schedule in memory and scans them in-process, so it is intended for moderate schedule sets rather than millions of schedule rows.

## Trigger a job immediately

Inject `IBackgroundJobScheduler` to trigger a job immediately or request a reload after the provider's schedules change:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Schedulite.Abstractions;

public sealed class ReminderController([FromKeyedServices(null)] IBackgroundJobScheduler scheduler)
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

After updating the data used by `IBackgroundJobScheduleProvider`, call `ReloadSchedulesAsync` to notify the scheduler:

```csharp
await scheduler.ReloadSchedulesAsync(cancellationToken);
```

Each execution receives a `BackgroundJobContext` containing an execution ID, job ID, subject ID, optional schedule ID, and whether the trigger was manual or scheduled. Jobs should honor the cancellation token.

## Runtime guarantees and limits

Each runtime uses a bounded, in-memory execution queue. Queued work is lost if the process exits before dispatch, and Schedulite does not persist execution state, retry failed jobs, or claim work across application replicas. Job failures are logged through `Microsoft.Extensions.Logging` when the application has logging configured; the dispatcher continues with later executions. Each replica schedules independently, so deploying multiple replicas can run the same schedule more than once. The concurrency scopes coordinate executions only within one runtime in one process. After 1,000 missed occurrences, Schedulite asks the provider to jump to the next occurrence after the current time to bound catch-up work; relative interval rules may rebase after a long outage. Use an application-owned durable queue and distributed coordination when jobs require persistence, retries, or cross-replica uniqueness.

This implementation provides in-process best-effort scheduling; it does not promise at-least-once or exactly-once job delivery.

## Configuration

`ScheduliteOptions` provides:

- `MaxConcurrency`: maximum number of jobs dispatched at once. Defaults to `4`.
- `ExecutionQueueCapacity`: maximum number of queued execution requests. Defaults to `100`.

Both values must be greater than zero.

## Example application

The [`Schedulite.Example`](https://github.com/nightmaregaurav/Schedulite/tree/main/Schedulite.Example) project is a runnable ASP.NET Core application showing dependency injection, multiple jobs, schedule-provider rules, manual triggering, and an API endpoint for inspecting schedules. Its trigger endpoint is intentionally a local demo and has no authentication configured; secure it before exposing it publicly. It is the best starting point for a complete integration:

```bash
dotnet run --project Schedulite.Example
```

The example uses deterministic schedule IDs and demonstrates application-owned schedule rules such as `every-minute` and `every-2-minute`.

## License

Schedulite is available under the [MIT License](LICENSE).
