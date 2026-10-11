using Microsoft.AspNetCore.Mvc;
using Schedulite.Abstractions;

namespace Schedulite.Example.Features.BackgroundJobManagement;

[ApiController]
[Route("api/background-jobs")]
public sealed class BackgroundJobManagementController(IScheduliteManager scheduleManager) : ControllerBase {
    [HttpGet]
    [ProducesResponseType<IEnumerable<BackgroundJobInfoModel>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListSchedules(CancellationToken cancellationToken)
    {
        var schedules = await scheduleManager.ListAsync(cancellationToken);
        var flattenedSchedules = schedules.SelectMany(runtime => runtime.Schedules).ToList();
        return Ok(flattenedSchedules);
    }

    [HttpPost("trigger/{jobId}")]
    [ProducesResponseType<Guid>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TriggerJob(
        [FromQuery] string? instanceId,
        [FromQuery] string jobId,
        [FromQuery] string? subjectId,
        CancellationToken cancellationToken
    ) {
        try
        {
            var executionId = await scheduleManager.TriggerAsync(instanceId, jobId, subjectId, cancellationToken);
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

    [HttpPost("reload-all")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ReloadAllSchedules(CancellationToken cancellationToken) {
        await scheduleManager.ReloadAllAsync(cancellationToken);
        return Accepted();
    }

    [HttpPost("reload")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ReloadSchedules(
        [FromQuery] string? instanceId,
        CancellationToken cancellationToken
    ) {
        await scheduleManager.ReloadAsync(instanceId, cancellationToken);
        return Accepted();
    }
}
