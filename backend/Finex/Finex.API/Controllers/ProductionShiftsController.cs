using Finex.API.Contracts.Common;
using Finex.API.Contracts.Production;
using Finex.API.Observability;
using Finex.API.Security;
using Finex.Application.DTOs.Production;
using Finex.Application.Interfaces.Production;
using Finex.Domain.Enums.Production;
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

    [HttpPost("reports/{reportId:int}/approve")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse>> ApproveShiftReport(
        [FromRoute] int reportId,
        [FromBody] ApproveShiftReportRequest request,
        CancellationToken cancellationToken)
    {
        using var activity = FinexDiagnostics.ActivitySource.StartActivity(
            "ProductionShiftsController.ApproveShiftReport",
            ActivityKind.Internal);

        activity?.SetTag("production.report_id", reportId);
        activity?.SetTag("production.reviewer_code", request.ReviewedBy);
        activity?.AddEvent(new ActivityEvent("Starting shift production report approval"));

        logger.LogInformation(
            "Attempting to approve shift production report {ReportId} by Reviewer {ReviewerCode}.",
            reportId,
            request.ReviewedBy);

        var result = await shiftService.ApproveShiftReportAsync(
            reportId,
            request.ReviewedBy,
            cancellationToken);

        if (result.IsFailure)
        {
            activity?.SetStatus(ActivityStatusCode.Error, result.Error.Code);
            activity?.SetTag("error.code", result.Error.Code);
            activity?.AddEvent(new ActivityEvent($"Shift report approval failed: {result.Error.Code}"));

            logger.LogWarning(
                "Failed to approve shift report {ReportId}. Reason: {ErrorCode} - {ErrorDescription}",
                reportId,
                result.Error.Code,
                result.Error.Description);

            return FromResult(result);
        }

        activity?.AddEvent(new ActivityEvent("Shift production report approved and locked successfully"));

        logger.LogInformation(
            "Shift report {ReportId} was approved by Reviewer {ReviewerCode}.",
            reportId,
            request.ReviewedBy);

        return FromResult(result, "The shift report has been approved and locked for inventory.");
    }

    // TODO: Enable [Authorize(Roles = "ProductionManager,Admin")] once JWT RBAC is configured
    [HttpGet("reports")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(PagedResponse<ShiftProductionReportSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResponse<ShiftProductionReportSummaryDto>>> GetShiftReports(
        [FromQuery] ShiftReportStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        using var activity = FinexDiagnostics.ActivitySource.StartActivity(
            "ProductionShiftsController.GetShiftReports",
            ActivityKind.Internal);

        activity?.SetTag("production.filter_status", status?.ToString() ?? "All");
        activity?.SetTag("pagination.page", page);
        activity?.SetTag("pagination.page_size", pageSize);
        activity?.AddEvent(new ActivityEvent("Starting shift production reports query"));

        var result = await shiftService.GetShiftReportsAsync(status, page, pageSize, cancellationToken);

        if (result.IsFailure)
        {
            activity?.SetStatus(ActivityStatusCode.Error, result.Error.Code);
            activity?.SetTag("error.code", result.Error.Code);
            activity?.AddEvent(new ActivityEvent($"Shift reports query failed: {result.Error.Code}"));

            logger.LogWarning(
                "Invalid shift reports query (Status: {Status}, Page: {Page}, PageSize: {PageSize}). Reason: {ErrorCode}",
                status,
                page,
                pageSize,
                result.Error.Code);

            return FromResult(result).Result!;
        }

        var pagedData = result.Value;
        activity?.SetTag("production.total_count", pagedData.TotalCount);
        activity?.AddEvent(new ActivityEvent("Shift production reports retrieved successfully"));

        return Ok(PagedResponse<ShiftProductionReportSummaryDto>.Ok(
            pagedData.Items,
            pagedData.Page,
            pagedData.PageSize,
            pagedData.TotalCount,
            "Shift production reports retrieved successfully."));
    }

    [HttpGet("reports/{reportId:int:min(1)}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(ApiResponse<ShiftProductionReportDetailsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ShiftProductionReportDetailsDto>>> GetShiftReportById(
        [FromRoute] int reportId,
        CancellationToken cancellationToken = default)
    {
        using var activity = FinexDiagnostics.ActivitySource.StartActivity(
            "ProductionShiftsController.GetShiftReportById",
            ActivityKind.Internal);

        activity?.SetTag("production.report_id", reportId);
        activity?.AddEvent(new ActivityEvent("Starting shift production report details lookup"));

        var result = await shiftService.GetShiftReportByIdAsync(reportId, cancellationToken);

        if (result.IsFailure)
        {
            activity?.SetStatus(ActivityStatusCode.Error, result.Error.Code);
            activity?.SetTag("error.code", result.Error.Code);
            activity?.AddEvent(new ActivityEvent($"Shift report lookup failed: {result.Error.Code}"));

            return FromResult(result);
        }

        activity?.AddEvent(new ActivityEvent("Shift production report details retrieved successfully"));
        return FromResult(result, "Shift production report retrieved successfully.");
    }
}