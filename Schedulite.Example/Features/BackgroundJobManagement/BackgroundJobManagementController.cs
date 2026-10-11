using Microsoft.AspNetCore.Mvc;
using Schedulite.Abstractions;

namespace Schedulite.Example.Features.BackgroundJobManagement;

[ApiController]
[Route("api/background-jobs")]
public sealed class BackgroundJobManagementController(
    [FromKeyedServices(null)] IBackgroundJobScheduler scheduler,
    [FromKeyedServices(null)] IBackgroundJobScheduleProvider scheduleProvider,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet("")]
    [ProducesResponseType<IEnumerable<BackgroundJobModel>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListSchedules(CancellationToken cancellationToken)
    {
        var schedules = await scheduleProvider.GetSchedulesAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var models = new List<BackgroundJobModel>();

        foreach (var schedule in schedules)
        {
            models.Add(new BackgroundJobModel
            {
                ScheduleId = schedule.ScheduleId,
                JobId = schedule.JobId,
                SubjectId = schedule.SubjectId,
                ScheduleRule = schedule.ScheduleRule,
                Enabled = schedule.Enabled,
                NextExecution = schedule.Enabled ? await scheduleProvider.GetNextExecution(schedule.ScheduleRule, now) : null
            });
        }

        return Ok(models);
    }

    [HttpPost("trigger/{jobId}")]
    [ProducesResponseType<Guid>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TriggerJob(
        [FromRoute] string jobId,
        [FromQuery] string? subjectId,
        CancellationToken cancellationToken)
    {
        try
        {
            var executionId = await scheduler.TriggerAsync(jobId, subjectId, cancellationToken);
            return Accepted(executionId);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException)
        {
            return BadRequest();
        }
    }

    [HttpPost("reload")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ReloadSchedules(CancellationToken cancellationToken)
    {
        await scheduler.ReloadSchedulesAsync(cancellationToken);
        return Accepted();
    }
}
