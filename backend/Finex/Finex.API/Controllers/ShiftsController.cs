using Finex.API.Contracts.Common;
using Finex.API.Observability;
using Finex.API.Security;
using Finex.Application.Common.Results;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace Finex.API.Controllers;

[Route("api/shifts")]
public sealed class ShiftsController(ILogger<ShiftsController> logger) : ApiControllerBase
{
    public sealed record ShiftSummaryDto(Guid ShiftId, DateTime EndedAtUtc, decimal TotalAmount);

    [HttpPost("end")]
    [RequireHmacSignature]
    [ProducesResponseType(typeof(ApiResponse<ShiftSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public ActionResult<ApiResponse<ShiftSummaryDto>> EndShift([FromBody] object dto)
    {
        using var activity = FinexDiagnostics.ActivitySource.StartActivity("ShiftsController.EndShift", ActivityKind.Internal);
        activity?.SetTag("shift", "end");
        activity?.AddEvent(new ActivityEvent("Processing shift end request"));
        logger.LogInformation("Processing shift end request.");

        var summary = new ShiftSummaryDto(Guid.NewGuid(), DateTime.UtcNow, 15400.00m);
        Result<ShiftSummaryDto> result = Result.Success(summary);

        return FromResult(result, "Shift ended successfully.");
    }

    [HttpPost("record")]
    [RequireHmacSignature]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public ActionResult<ApiResponse> RecordShift([FromBody] object dto)
    {
        Result result = Result.Success();
        return FromResult(result, "Shift recorded successfully.");
    }

    [HttpGet("report")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ShiftSummaryDto>>), StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<IReadOnlyList<ShiftSummaryDto>>> GetShiftReport()
    {
        IReadOnlyList<ShiftSummaryDto> reports = [];
        return OkResponse(reports, "Shift report retrieved successfully.");
    }
}