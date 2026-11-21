using Finex.API.Contracts.Common;
using Finex.API.Observability;
using Finex.API.Security;
using Finex.Application.DTOs;
using Finex.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace Finex.API.Controllers;

[Route("api/production-shifts")]
public sealed class ProductionShiftsController(
    IProductionShiftService shiftService,
    ILogger<ProductionShiftsController> logger) : ApiControllerBase
{
    [HttpPost("reports")]
    [RequireHmacSignature]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<int>>> SubmitShiftReport(
        [FromBody] ShiftProductionReportDto dto,
        CancellationToken cancellationToken)
    {
        using var activity = FinexDiagnostics.ActivitySource.StartActivity(
            "ProductionShiftsController.SubmitShiftReport",
            ActivityKind.Internal);

        activity?.SetTag("production.plant_code", dto.PlantCode);
        activity?.SetTag("production.line_code", dto.LineCode);
        activity?.SetTag("production.shift_code", dto.ShiftCode);
        activity?.SetTag("production.lines_count", dto.ProducedLines.Count);
        activity?.AddEvent(new ActivityEvent("Starting shift production report ingestion"));

        logger.LogInformation(
            "Received shift production report for Plant {PlantCode}, Line {LineCode}, Shift {ShiftCode} on {ProductionDate}.",
            dto.PlantCode,
            dto.LineCode,
            dto.ShiftCode,
            dto.ProductionDate);

        var result = await shiftService.IngestShiftReportAsync(dto, cancellationToken);

        if (result.IsFailure)
        {
            activity?.SetStatus(ActivityStatusCode.Error, result.Error.Code);
            activity?.SetTag("error.code", result.Error.Code);
            activity?.AddEvent(new ActivityEvent($"Shift report rejected: {result.Error.Code}"));

            logger.LogWarning(
                "Rejected shift production report for Plant {PlantCode}, Shift {ShiftCode}. Reason: {ErrorCode} - {ErrorDescription}",
                dto.PlantCode,
                dto.ShiftCode,
                result.Error.Code,
                result.Error.Description);

            return FromResult(result);
        }

        activity?.SetTag("production.report_id", result.Value);
        activity?.AddEvent(new ActivityEvent("Shift production report persisted successfully"));

        logger.LogInformation(
            "Successfully persisted shift production report {ReportId} for Plant {PlantCode}, Shift {ShiftCode}.",
            result.Value,
            dto.PlantCode,
            dto.ShiftCode);

        return FromResult(result, "The shift report has been successfully received and saved.");
    }
}