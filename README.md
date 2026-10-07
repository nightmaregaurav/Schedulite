/*
* This class contains extension methods for IServiceCollection to register Schedulite services and jobs.
*
* Example usage:
*
* program.cs:
* ```csharp
* builder.Services.AddSchedulite<BackgroundJobScheduleProvider>(options =>
* {
*     options.MaxConcurrency = 4;
*     options.ExecutionQueueCapacity = 100;
* });
* builder.Services.AddScheduliteJob<SayHiJob>("SayHiJob");
* ```
*
* BackgroundJobScheduleProvider.cs:
* ```csharp
* using Schedulite;

  public class BackgroundJobScheduleProvider : IBackgroundJobScheduleProvider
  {
  public Task<IReadOnlyCollection<BackgroundJobSchedule>> GetSchedulesAsync(CancellationToken cancellationToken = default)
  {
  try
  {
  return Task.FromResult<IReadOnlyCollection<BackgroundJobSchedule>>(new List<BackgroundJobSchedule>
  {
  new()
  {
  ScheduleId = Guid.NewGuid().ToString(),
  JobId = "SayHiJob",
  SubjectId = "1",
  ScheduleRule = "whatever rule I set later",
  Enabled = true
  }
  });
  }
  catch (Exception exception)
  {
  return Task.FromException<IReadOnlyCollection<BackgroundJobSchedule>>(exception);
  }
  }

      public Task<DateTimeOffset?> GetNextExecution(string scheduleRule, DateTimeOffset now)
      {
          try
          {
              Console.WriteLine($"GetNextExecution called with scheduleRule: {scheduleRule} and now: {now}");
              // For demonstration purposes, let's just return a time 1 minute from now
              return Task.FromResult<DateTimeOffset?>(now.AddMinutes(1));
          }
          catch (Exception exception)
          {
              return Task.FromException<DateTimeOffset?>(exception);
          }
      }
  }
*
* SayHiJob.cs:
* ```csharp
* using System.Text.Json;
* using Schedulite;
*
* public class SayHiJob : IBackgroundJob
* {
*     public string JobName => "Say Hi";
*     public string JobDescription => "Simple job that says hi and simulates work by waiting for 5 seconds.";
*
*     public Task ExecuteAsync(BackgroundJobContext context, CancellationToken cancellationToken)
*     {
*         try
*         {
*             Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}: Hello from SayHiJob! Context: {JsonSerializer.Serialize(context)}");
*             // wait for 5 seconds to simulate some work
*             Task.Delay(5000, cancellationToken).Wait(cancellationToken);
*             Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}: SayHiJob completed.");
*             return Task.CompletedTask;
*         }
*         catch (Exception exception)
*         {
*             return Task.FromException(exception);
*         }
*     }
* }
* ```
*
* JobController.cs:
* ```csharp
* using Schedulite;
*
* public class EventsController(IBackgroundJobScheduler backgroundJobScheduler) : ControllerBase
  {
  [HttpDelete]
  [Route("hi")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [EndpointDescription("Says Hi")]
  public async Task<IActionResult> SayHi()
  {
  await backgroundJobScheduler.TriggerAsync("SayHiJob", "1");
  Console.WriteLine("Triggered the 'say-hi-job' background job.");
  return StatusCode(StatusCodes.Status200OK);
  }
  }
*
*/
