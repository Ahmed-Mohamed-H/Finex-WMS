using Finex.API.Contracts.Common;
using Finex.API.Contracts.Production;
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
        activity?.SetTag("production.lines_count", dto.ProducedLines?.Count ?? 0);
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
            activity?.AddEvent(new ActivityEvent($"Shift report ingestion failed: {result.Error.Code}"));

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

    [HttpPost("reports/{reportId:int}/reject")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse>> RejectShiftReport(
        [FromRoute] int reportId,
        [FromBody] RejectShiftReportRequest request,
        CancellationToken cancellationToken)
    {
        using var activity = FinexDiagnostics.ActivitySource.StartActivity(
            "ProductionShiftsController.RejectShiftReport",
            ActivityKind.Internal);

        activity?.SetTag("production.report_id", reportId);
        activity?.SetTag("production.reviewer_code", request.ReviewedBy);
        activity?.AddEvent(new ActivityEvent("Starting shift production report rejection"));

        logger.LogInformation(
            "Attempting to reject shift production report {ReportId} by Reviewer {ReviewerCode}.",
            reportId,
            request.ReviewedBy);

        var result = await shiftService.RejectShiftReportAsync(
            reportId,
            request.RejectionReason,
            request.ReviewedBy,
            cancellationToken);

        if (result.IsFailure)
        {
            activity?.SetStatus(ActivityStatusCode.Error, result.Error.Code);
            activity?.SetTag("error.code", result.Error.Code);
            activity?.AddEvent(new ActivityEvent($"Shift report rejection failed: {result.Error.Code}"));

            logger.LogWarning(
                "Failed to reject shift report {ReportId}. Reason: {ErrorCode} - {ErrorDescription}",
                reportId,
                result.Error.Code,
                result.Error.Description);

            return FromResult(result);
        }

        activity?.AddEvent(new ActivityEvent("Shift production report rejected and unlocked successfully"));

        logger.LogInformation(
            "Shift report {ReportId} was rejected by Reviewer {ReviewerCode}.",
            reportId,
            request.ReviewedBy);

        return FromResult(result, "The shift report has been rejected and unlocked for resubmission.");
    }
}