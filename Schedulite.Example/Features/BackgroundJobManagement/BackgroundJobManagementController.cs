using Microsoft.AspNetCore.Mvc;
using Schedulite.Abstractions;

namespace Schedulite.Example.Features.BackgroundJobManagement;

[ApiController]
[Route("api/background-jobs")]
public sealed class BackgroundJobManagementController(
    IBackgroundJobScheduler scheduler,
    IBackgroundJobScheduleProvider scheduleProvider,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet("")]
    [ProducesResponseType<IEnumerable<BackgroundJobModel>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListServices(CancellationToken cancellationToken)
    {
        var schedules = await scheduleProvider.GetSchedulesAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var services = new List<BackgroundJobModel>();

        foreach (var schedule in schedules)
        {
            services.Add(new BackgroundJobModel
            {
                ScheduleId = schedule.ScheduleId,
                JobId = schedule.JobId,
                SubjectId = schedule.SubjectId,
                ScheduleRule = schedule.ScheduleRule,
                Enabled = schedule.Enabled,
                NextExecution = schedule.Enabled ? await scheduleProvider.GetNextExecution(schedule.ScheduleRule, now) : null
            });
        }

        return Ok(services);
    }

    [HttpPost("trigger/{serviceId}")]
    [ProducesResponseType<Guid>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TriggerService([FromRoute] string serviceId, CancellationToken cancellationToken)
    {
        try
        {
            var executionId = await scheduler.TriggerAsync(serviceId, "tenant-can-be-added-here", cancellationToken);
            return Accepted(executionId);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}
