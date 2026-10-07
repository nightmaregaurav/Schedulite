using Microsoft.AspNetCore.Mvc;

namespace Schedulite.Example.Features;

[ApiController]
[Route("api/example")]
public class ExampleController : ControllerBase
{
    [HttpGet]
    [Route("trigger/{serviceId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [EndpointDescription($"Triggers the background service identified by the provided serviceId immediately. Returns a 200 OK status code upon successful triggering.")]
    public async Task<IActionResult> TriggerService([FromRoute] string serviceId)
    {
        return StatusCode(StatusCodes.Status200OK);
    }
}
