using Finex.API.Contracts.Common;
using Microsoft.AspNetCore.Mvc;

namespace Finex.API.Controllers;

[Route("api/test-diagnostics")]
public sealed class DiagnosticsTestController : ApiControllerBase
{
    public sealed record TestPayload(string WarehouseCode, int Quantity);

    [HttpGet("crash")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public IActionResult TriggerCrash()
    {
        throw new InvalidOperationException("Simulated critical failure inside business layer!");
    }

    [HttpGet("slow-cancel")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> TriggerSlowOperation(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), ct);
        return OkResponse<object>(new { Status = "Completed" });
    }

    [HttpPost("bad-json")]
    [ProducesResponseType(typeof(ApiResponse<TestPayload>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public ActionResult<ApiResponse<TestPayload>> TriggerBadJson([FromBody] TestPayload payload)
    {
        return OkResponse(payload);
    }

    [HttpGet("stream-crash")]
    public async Task TriggerStreamCrash(CancellationToken ct)
    {
        Response.ContentType = "text/plain";
        await Response.WriteAsync("Chunk 1 sent over the wire...\n", ct);
        await Response.Body.FlushAsync(ct);

        throw new InvalidOperationException("Crash mid-stream after response had already started!");
    }
}