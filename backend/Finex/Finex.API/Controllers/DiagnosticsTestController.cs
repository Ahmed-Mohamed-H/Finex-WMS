using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Finex.API.Controllers
{
    [ApiController]
    [Route("api/test-diagnostics")]
    public sealed class DiagnosticsTestController : ControllerBase
    {
        public sealed record TestPayload(string WarehouseCode, int Quantity);

        [HttpGet("crash")]
        public IActionResult TriggerCrash()
        {
            throw new InvalidOperationException("Simulated critical failure inside business layer!");
            
        }

        [HttpGet("slow-cancel")]
        public async Task<IActionResult> TriggerSlowOperation(CancellationToken ct)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return Ok(new { Status = "Completed" });
        }

        [HttpPost("bad-json")]
        public IActionResult TriggerBadJson([FromBody] TestPayload payload)
        {
            return Ok(payload);
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
}
