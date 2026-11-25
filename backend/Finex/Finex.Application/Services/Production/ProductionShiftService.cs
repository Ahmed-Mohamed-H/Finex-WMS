using Finex.Application.Common.Models;
using Finex.Application.Common.Results;
using Finex.Application.DTOs.Production;
using Finex.Application.Interfaces;
using Finex.Application.Interfaces.Production;
using Finex.Application.Mappings;
using Finex.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using ZiggyCreatures.Caching.Fusion;

namespace Finex.Application.Services.Production;

public sealed class ProductionShiftService(
    IFinexDbContext dbContext,
    IFusionCache fusionCache,
    ILogger<ProductionShiftService> logger) : IProductionShiftService
{
    private const string ReportsCacheTag = "production:reports";

    public async Task<Result<int>> IngestShiftReportAsync(
        ShiftProductionReportDto dto,
        CancellationToken cancellationToken = default)
    {
        using var activity = Activity.Current?.Source.StartActivity(
            "ProductionShiftService.IngestShiftReport",
            ActivityKind.Internal);

        activity?.SetTag("production.plant_code", dto.PlantCode);
        activity?.SetTag("production.line_code", dto.LineCode);
        activity?.SetTag("production.shift_code", dto.ShiftCode);
        activity?.SetTag("production.date", dto.ProductionDate.ToString("yyyy-MM-dd"));

        try
        {
            var txResult = await dbContext.ExecuteInTransactionAsync<Result<int>>(async ct =>
            {
                var existingReports = await dbContext.ShiftProductionReports
                    .Where(r =>
                        r.PlantCode == dto.PlantCode &&
                        r.LineCode == dto.LineCode &&
                        r.ShiftCode == dto.ShiftCode &&
                        r.ProductionDate == dto.ProductionDate)
                    .OrderByDescending(r => r.RevisionNumber)
                    .ToListAsync(ct);

                if (existingReports.Any(r => r.Status == ShiftReportStatus.Approved))
                {
                    return Error.Conflict(
                        "ProductionShift.AlreadyApproved",
                        $"Shift report for {dto.PlantCode}/{dto.LineCode}/{dto.ShiftCode} on {dto.ProductionDate:yyyy-MM-dd} is already approved and locked.");
                }

                if (existingReports.Any(r => r.Status == ShiftReportStatus.PendingReview))
                {
                    return Error.Conflict(
                        "ProductionShift.DuplicatePendingReport",
                        $"An active shift report for {dto.PlantCode}/{dto.LineCode}/{dto.ShiftCode} on {dto.ProductionDate:yyyy-MM-dd} is already pending manager review.");
                }

                var latestRejected = existingReports.FirstOrDefault(r => r.Status == ShiftReportStatus.Rejected);
                var nextRevision = latestRejected is null ? 1 : latestRejected.RevisionNumber + 1;

                // استخدام Mapperly بدلاً من بناء الـ Entity يدوياً
                var report = dto.ToEntity();
                report.RevisionNumber = nextRevision;

                dbContext.ShiftProductionReports.Add(report);
                await dbContext.SaveChangesAsync(ct);

                if (latestRejected is not null)
                {
                    latestRejected.SupersededByReportId = report.Id;
                    await dbContext.SaveChangesAsync(ct);
                }

                activity?.SetTag("production.report_id", report.Id);
                activity?.SetTag("production.revision_number", report.RevisionNumber);

                return report.Id;
            }, cancellationToken);

            if (txResult.IsFailure)
            {
                activity?.SetStatus(ActivityStatusCode.Error, txResult.Error.Code);
                logger.LogWarning(
                    "Shift report ingestion rejected ({ErrorCode}) for {Plant}/{Line}/{Shift} on {Date}.",
                    txResult.Error.Code,
                    dto.PlantCode,
                    dto.LineCode,
                    dto.ShiftCode,
                    dto.ProductionDate);

                return txResult;
            }

            await InvalidateReportsCacheAsync(cancellationToken);

            logger.LogInformation(
                "Ingested shift report {ReportId} (Rev {Revision}) for {Plant}/{Line}/{Shift} on {Date} with {LineCount} produced lines.",
                txResult.Value,
                activity?.GetTagItem("production.revision_number"),
                dto.PlantCode,
                dto.LineCode,
                dto.ShiftCode,
                dto.ProductionDate,
                dto.ProducedLines.Count);

            return txResult;
        }
        catch (DbUpdateException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "ProductionShift.ConcurrencyConflict");
            logger.LogWarning(
                ex,
                "Database constraint conflict while ingesting shift report for {Plant}/{Line}/{Shift} on {Date}.",
                dto.PlantCode,
                dto.LineCode,
                dto.ShiftCode,
                dto.ProductionDate);

            return Error.Conflict(
                "ProductionShift.ConcurrencyConflict",
                "A duplicate active shift report or duplicate SSCC barcode was detected concurrently.");
        }
    }

    public async Task<Result> RejectShiftReportAsync(
        int reportId,
        string rejectionReason,
        string reviewedBy,
        CancellationToken cancellationToken = default)
    {
        using var activity = Activity.Current?.Source.StartActivity(
            "ProductionShiftService.RejectShiftReport",
            ActivityKind.Internal);

        activity?.SetTag("production.report_id", reportId);
        activity?.SetTag("production.reviewed_by", reviewedBy);

        if (reportId < 1)
        {
            return Error.Validation(
                "ProductionShift.InvalidReportId",
                "Report ID must be a positive integer.");
        }

        if (string.IsNullOrWhiteSpace(rejectionReason))
        {
            return Error.Validation(
                "ProductionShift.RejectionReasonRequired",
                "Rejection reason is required.");
        }

        if (string.IsNullOrWhiteSpace(reviewedBy))
        {
            return Error.Validation(
                "ProductionShift.ReviewerRequired",
                "Reviewer identifier is required.");
        }

        var txResult = await dbContext.ExecuteInTransactionAsync<Result>(async ct =>
        {
            var report = await dbContext.ShiftProductionReports
                .Include(r => r.ProducedLines)
                .ThenInclude(l => l.SsccBarcodes)
                .FirstOrDefaultAsync(r => r.Id == reportId, ct);

            if (report is null)
            {
                return Error.NotFound(
                    "ProductionShift.NotFound",
                    $"Shift production report with ID '{reportId}' was not found.");
            }

            if (report.Status != ShiftReportStatus.PendingReview)
            {
                return Error.Conflict(
                    "ProductionShift.InvalidStateTransition",
                    $"Shift report '{reportId}' cannot be rejected because its current status is '{report.Status}'.");
            }

            report.Status = ShiftReportStatus.Rejected;
            report.RejectionReason = rejectionReason.Trim();
            report.ReviewedBy = reviewedBy.Trim();
            report.ReviewedAtUtc = DateTimeOffset.UtcNow;

            foreach (var barcode in report.ProducedLines.SelectMany(l => l.SsccBarcodes))
            {
                barcode.IsActive = false;
            }

            await dbContext.SaveChangesAsync(ct);

            return Result.Success();
        }, cancellationToken);

        if (txResult.IsFailure)
        {
            activity?.SetStatus(ActivityStatusCode.Error, txResult.Error.Code);
            logger.LogWarning(
                "Failed to reject shift report {ReportId} by {ReviewedBy}. Reason: {ErrorCode}",
                reportId,
                reviewedBy,
                txResult.Error.Code);

            return txResult;
        }

        await InvalidateReportsCacheAsync(cancellationToken);

        logger.LogInformation(
            "Shift report {ReportId} was rejected by {ReviewedBy}.",
            reportId,
            reviewedBy);

        return Result.Success();
    }

    public async Task<Result<PagedData<ShiftProductionReportSummaryDto>>> GetShiftReportsAsync(
        ShiftReportStatus? status = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        using var activity = Activity.Current?.Source.StartActivity(
            "ProductionShiftService.GetShiftReports",
            ActivityKind.Internal);

        if (page < 1)
        {
            return Error.Validation(
                "ProductionShift.InvalidPage",
                "Page number must be greater than or equal to 1.");
        }

        if (pageSize is < 1 or > 100)
        {
            return Error.Validation(
                "ProductionShift.InvalidPageSize",
                "Page size must be between 1 and 100.");
        }

        if (status.HasValue && !Enum.IsDefined(status.Value))
        {
            logger.LogWarning(
                "Rejected shift reports query due to undefined status filter: {StatusValue}",
                (int)status.Value);

            return Error.Validation(
                "ProductionShift.InvalidStatus",
                "The specified shift report status filter is invalid.");
        }

        var statusKey = status?.ToString() ?? "All";
        var cacheKey = $"production:reports:list:{statusKey}:p{page}:s{pageSize}";

        activity?.SetTag("production.filter_status", statusKey);
        activity?.SetTag("pagination.page", page);
        activity?.SetTag("pagination.page_size", pageSize);

        var dbQueried = false;

        var pagedResult = await fusionCache.GetOrSetAsync<PagedData<ShiftProductionReportSummaryDto>>(
            key: cacheKey,
            factory: async (_, ct) =>
            {
                dbQueried = true;
                activity?.AddEvent(new ActivityEvent("Cache miss: querying SQL Server for shift reports list"));

                var baseQuery = dbContext.ShiftProductionReports.AsNoTracking();

                if (status.HasValue)
                {
                    baseQuery = baseQuery.Where(r => r.Status == status.Value);
                }

                var totalCount = await baseQuery.CountAsync(ct);

                var items = totalCount == 0
                    ? []
                    : await baseQuery
                        .OrderByDescending(r => r.ProductionDate)
                        .ThenByDescending(r => r.Id)
                        .Skip((page - 1) * pageSize)
                        .Take(pageSize)
                        .ProjectToSummaryDto()
                        .ToListAsync(ct);

                logger.LogInformation(
                    "Loaded {ReturnedCount} of {TotalCount} shift reports from SQL Server (Status: {Status}, Page: {Page}).",
                    items.Count,
                    totalCount,
                    statusKey,
                    page);

                return new PagedData<ShiftProductionReportSummaryDto>(
                    items,
                    page,
                    pageSize,
                    totalCount);
            },
            options => options
                .SetDuration(TimeSpan.FromMinutes(5))
                .SetEagerRefresh(0.8f)
                .SetFailSafe(
                    isEnabled: true,
                    maxDuration: TimeSpan.FromMinutes(30),
                    throttleDuration: TimeSpan.FromSeconds(10))
                .SetFactoryTimeouts(
                    softTimeout: TimeSpan.FromMilliseconds(250),
                    hardTimeout: TimeSpan.FromSeconds(5)),
            tags: [ReportsCacheTag],
            token: cancellationToken);

        activity?.SetTag("cache.hit", !dbQueried);
        activity?.SetTag("production.total_count", pagedResult.TotalCount);

        return pagedResult;
    }

    public async Task<Result<ShiftProductionReportDetailsDto>> GetShiftReportByIdAsync(
        int reportId,
        CancellationToken cancellationToken = default)
    {
        using var activity = Activity.Current?.Source.StartActivity(
            "ProductionShiftService.GetShiftReportById",
            ActivityKind.Internal);

        activity?.SetTag("production.report_id", reportId);

        if (reportId < 1)
        {
            return Error.Validation(
                "ProductionShift.InvalidReportId",
                "Report ID must be a positive integer.");
        }

        var cacheKey = $"production:reports:details:{reportId}";
        var dbQueried = false;

        var report = await fusionCache.GetOrSetAsync<ShiftProductionReportDetailsDto?>(
            key: cacheKey,
            factory: async (ctx, ct) =>
            {
                dbQueried = true;
                activity?.AddEvent(new ActivityEvent("Cache miss: querying SQL Server for report details"));

                var entityDto = await dbContext.ShiftProductionReports
                    .AsNoTracking()
                    .Where(r => r.Id == reportId)
                    .ProjectToDetailsDto()
                    .FirstOrDefaultAsync(ct);

                if (entityDto is null)
                {
                    ctx.Options.SetDuration(TimeSpan.Zero);
                    return null;
                }

                logger.LogInformation(
                    "Loaded shift report {ReportId} details ({LinesCount} lines) from SQL Server.",
                    reportId,
                    entityDto.ProducedLines.Count);

                return entityDto;
            },
            options => options
                .SetDuration(TimeSpan.FromMinutes(5))
                .SetEagerRefresh(0.8f)
                .SetFailSafe(
                    isEnabled: true,
                    maxDuration: TimeSpan.FromMinutes(30),
                    throttleDuration: TimeSpan.FromSeconds(10))
                .SetFactoryTimeouts(
                    softTimeout: TimeSpan.FromMilliseconds(250),
                    hardTimeout: TimeSpan.FromSeconds(5)),
            tags: [ReportsCacheTag],
            token: cancellationToken);

        activity?.SetTag("cache.hit", !dbQueried);

        if (report is null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "ProductionShift.NotFound");
            logger.LogWarning("Shift production report {ReportId} was not found.", reportId);

            return Error.NotFound(
                "ProductionShift.NotFound",
                $"Shift production report with ID '{reportId}' was not found.");
        }

        return report;
    }

    public async Task<Result> ApproveShiftReportAsync(
        int reportId,
        string reviewedBy,
        CancellationToken cancellationToken = default)
    {
        using var activity = Activity.Current?.Source.StartActivity(
            "ProductionShiftService.ApproveShiftReport",
            ActivityKind.Internal);

        activity?.SetTag("production.report_id", reportId);
        activity?.SetTag("production.reviewed_by", reviewedBy);

        if (reportId < 1)
        {
            return Error.Validation(
                "ProductionShift.InvalidReportId",
                "Report ID must be a positive integer.");
        }

        if (string.IsNullOrWhiteSpace(reviewedBy))
        {
            return Error.Validation(
                "ProductionShift.ReviewerRequired",
                "Reviewer identifier is required.");
        }

        var txResult = await dbContext.ExecuteInTransactionAsync<Result>(async ct =>
        {
            var report = await dbContext.ShiftProductionReports
                .FirstOrDefaultAsync(r => r.Id == reportId, ct);

            if (report is null)
            {
                return Error.NotFound(
                    "ProductionShift.NotFound",
                    $"Shift production report with ID '{reportId}' was not found.");
            }

            if (report.Status != ShiftReportStatus.PendingReview)
            {
                return Error.Conflict(
                    "ProductionShift.InvalidStateTransition",
                    $"Shift report '{reportId}' cannot be approved because its current status is '{report.Status}'.");
            }

            report.Status = ShiftReportStatus.Approved;
            report.ReviewedBy = reviewedBy.Trim();
            report.ReviewedAtUtc = DateTimeOffset.UtcNow;

            await dbContext.SaveChangesAsync(ct);

            return Result.Success();
        }, cancellationToken);

        if (txResult.IsFailure)
        {
            activity?.SetStatus(ActivityStatusCode.Error, txResult.Error.Code);
            logger.LogWarning(
                "Failed to approve shift report {ReportId} by {ReviewedBy}. Reason: {ErrorCode}",
                reportId,
                reviewedBy,
                txResult.Error.Code);

            return txResult;
        }

        await InvalidateReportsCacheAsync(cancellationToken);

        logger.LogInformation(
            "Shift report {ReportId} was approved by {ReviewedBy}.",
            reportId,
            reviewedBy);

        return Result.Success();
    }

    private async Task InvalidateReportsCacheAsync(CancellationToken cancellationToken)
    {
        await fusionCache.RemoveByTagAsync(ReportsCacheTag, token: cancellationToken);
        logger.LogDebug("Invalidated FusionCache entries for tag {CacheTag}.", ReportsCacheTag);
    }
}