using Finex.API.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Finex.API.Controllers
{
    [ApiController]
    [Route("api/shifts")]
    public sealed class ShiftsController : ControllerBase
    {
        [HttpPost("end")]
        [RequireHmacSignature]
        public IActionResult EndShift([FromBody] object dto)
        {
            return Ok(new { message = "Shift ended successfully" });
        }

        // ✅ سيمر على ApiSecurityMiddleware
        [HttpPost("record")]
        [RequireHmacSignature]
        public IActionResult RecordShift([FromBody] object dto)
        {
            return Ok(new { message = "Shift recorded successfully" });
        }

        // ❌ طلب عادي للـ ERP: لن يدخل ApiSecurityMiddleware نهائياً
        [HttpGet("report")]
        public IActionResult GetShiftReport()
        {
            return Ok();
        }
    }
}
