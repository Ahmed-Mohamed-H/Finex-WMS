using Finex.API.Middlewares;
using Finex.API.Observability;
using Finex.API.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace Finex.API.Controllers
{
    [ApiController]
    [Route("api/shifts")]

    public sealed class ShiftsController : ControllerBase
    {
        private readonly ILogger<ShiftsController> _logger;
        public ShiftsController(ILogger<ShiftsController> logger)
        {
            _logger = logger;
        }
        [HttpPost("end")]
        [RequireHmacSignature]
        public IActionResult EndShift([FromBody] object dto)
        {
            using var Activity = FinexDiagnostics.ActivitySource.StartActivity("ShiftsController.EndShift", System.Diagnostics.ActivityKind.Internal);
            Activity?.SetTag("shift", "end");
            Activity?.AddEvent(new ActivityEvent("Processing shift end request"));
            _logger.LogInformation("Processing shift end request.");
            return Ok(new { message = "Shift ended successfully" });
        }

        [HttpPost("record")]
        [RequireHmacSignature]
        public IActionResult RecordShift([FromBody] object dto)
        {
            return Ok(new { message = "Shift recorded successfully" });
        }

        [HttpGet("report")]
        public IActionResult GetShiftReport()
        {
            return Ok();
        }
    }
}
