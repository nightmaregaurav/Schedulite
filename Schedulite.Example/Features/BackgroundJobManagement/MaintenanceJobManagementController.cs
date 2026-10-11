using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Schedulite.Abstractions;

namespace Schedulite.Example.Features.BackgroundJobManagement;

[ApiController]
[Route("api/maintenance-jobs")]
public sealed class MaintenanceJobManagementController(
    [FromKeyedServices("maintenance")] IBackgroundJobScheduler scheduler,
    [FromKeyedServices("maintenance")] IBackgroundJobScheduleProvider scheduleProvider) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IEnumerable<BackgroundJobSchedule>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListSchedules(CancellationToken cancellationToken)
    {
        return Ok(await scheduleProvider.GetSchedulesAsync(cancellationToken));
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
            return Accepted(await scheduler.TriggerAsync(jobId, subjectId, cancellationToken));
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
