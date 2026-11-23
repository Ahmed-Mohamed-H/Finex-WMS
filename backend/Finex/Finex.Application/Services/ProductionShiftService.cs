using Finex.Application.Common.Results;
using Finex.Application.DTOs;
using Finex.Application.Interfaces;
using Finex.Domain.Entities;
using Finex.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Finex.Application.Services;

public sealed class ProductionShiftService(IFinexDbContext context) : IProductionShiftService
{
    public async Task<Result<int>> IngestShiftReportAsync(
        ShiftProductionReportDto dto,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dto.PlantCode) ||
            string.IsNullOrWhiteSpace(dto.LineCode) ||
            string.IsNullOrWhiteSpace(dto.ShiftCode))
        {
            return Error.Validation(
                "ProductionShift.MissingRequiredCodes",
                "Plant code, line code, and shift code are required.");
        }

        if (dto.ShiftEndedAt <= dto.ShiftStartedAt)
        {
            return Error.Validation(
                "ProductionShift.InvalidTimeRange",
                "Shift end time must be after shift start time.");
        }

        if (dto.ProducedLines is null || dto.ProducedLines.Count == 0)
        {
            return Error.Validation(
                "ProductionShift.EmptyLines",
                "Cannot record a shift report with no produced lines.");
        }

        var uniqueBarcodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in dto.ProducedLines)
        {
            if (string.IsNullOrWhiteSpace(line.Sku))
            {
                return Error.Validation(
                    "ProducedLine.MissingSku",
                    "SKU is required for every produced line.");
            }

            if (line.Quantity <= 0)
            {
                return Error.Validation(
                    "ProducedLine.InvalidQuantity",
                    $"Quantity for SKU '{line.Sku.Trim()}' must be greater than zero.");
            }

            if (line.ExpiryDate < line.ProductionDate)
            {
                return Error.Validation(
                    "ProducedLine.InvalidExpiryDate",
                    $"Expiry date for SKU '{line.Sku.Trim()}' cannot be earlier than production date.");
            }

            if (line.SsccBarcodes is not null)
            {
                foreach (var rawBarcode in line.SsccBarcodes)
                {
                    var barcode = rawBarcode?.Trim() ?? string.Empty;
                    if (string.IsNullOrEmpty(barcode))
                    {
                        return Error.Validation(
                            "ProducedLine.EmptySscc",
                            $"Empty SSCC barcode found in SKU '{line.Sku.Trim()}'.");
                    }

                    if (!uniqueBarcodes.Add(barcode))
                    {
                        return Error.Conflict(
                            "ProducedLine.DuplicateSsccInReport",
                            $"Pallet barcode '{barcode}' is duplicated within the same report.");
                    }
                }
            }
        }

        var plantCode = dto.PlantCode.Trim();
        var lineCode = dto.LineCode.Trim();
        var shiftCode = dto.ShiftCode.Trim();

        var activeShiftStatus = await context.ShiftProductionReports
            .Where(x => x.PlantCode == plantCode
                     && x.LineCode == lineCode
                     && x.ProductionDate == dto.ProductionDate
                     && x.ShiftCode == shiftCode
                     && x.Status != ShiftReportStatus.Rejected)
            .Select(x => (ShiftReportStatus?)x.Status)
            .FirstOrDefaultAsync(cancellationToken);

        if (activeShiftStatus == ShiftReportStatus.PendingReview)
        {
            return Error.Conflict(
                "ProductionShift.PendingReviewExists",
                "A report for this shift is already awaiting manager review.");
        }

        if (activeShiftStatus == ShiftReportStatus.Approved)
        {
            return Error.Conflict(
                "ProductionShift.AlreadyApproved",
                "A report for this shift has already been approved and locked.");
        }

        if (uniqueBarcodes.Count > 0)
        {
            var existingBarcode = await context.ProducedLineSsccBarcodes
                .Where(x => x.IsActive && uniqueBarcodes.Contains(x.SsccBarcode))
                .Select(x => x.SsccBarcode)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingBarcode is not null)
            {
                return Error.Conflict(
                    "ProducedLine.SsccAlreadyExists",
                    $"Pallet barcode '{existingBarcode}' is already recorded in an active report.");
            }
        }

        var lastRejectedReport = await context.ShiftProductionReports
            .Where(x => x.PlantCode == plantCode
                     && x.LineCode == lineCode
                     && x.ProductionDate == dto.ProductionDate
                     && x.ShiftCode == shiftCode
                     && x.Status == ShiftReportStatus.Rejected
                     && x.SupersededByReportId == null)
            .OrderByDescending(x => x.RevisionNumber)
            .FirstOrDefaultAsync(cancellationToken);

        int nextRevision = (lastRejectedReport?.RevisionNumber ?? 0) + 1;

        var reportEntity = new ShiftProductionReport
        {
            PlantCode = plantCode,
            LineCode = lineCode,
            ShiftCode = shiftCode,
            ProductionDate = dto.ProductionDate,
            ShiftStartedAt = dto.ShiftStartedAt,
            ShiftEndedAt = dto.ShiftEndedAt,
            ShiftSupervisorCode = dto.ShiftSupervisorCode?.Trim() ?? string.Empty,
            Status = ShiftReportStatus.PendingReview,
            RevisionNumber = nextRevision,
            ProducedLines = dto.ProducedLines.Select(line => new ProducedLine
            {
                Sku = line.Sku.Trim().ToUpperInvariant(),
                UnitName = line.UnitName?.Trim() ?? string.Empty,
                UnitBarcode = line.UnitBarcode?.Trim() ?? string.Empty,
                Quantity = line.Quantity,
                ProductionDate = line.ProductionDate,
                ExpiryDate = line.ExpiryDate,
                SsccBarcodes = (line.SsccBarcodes ?? [])
                    .Select(code => new ProducedLineSsccBarcode
                    {
                        SsccBarcode = code.Trim(),
                        IsActive = true
                    })
                    .ToList()
            }).ToList()
        };

        context.ShiftProductionReports.Add(reportEntity);

        // ربط النسخة المرفوضة بالجديدة عبر الـ Navigation Property ليحفظهم EF Core في Transaction واحدة
        if (lastRejectedReport is not null)
        {
            lastRejectedReport.SupersededByReport = reportEntity;
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return reportEntity.Id;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            var message = ex.InnerException?.Message ?? ex.Message;

            if (message.Contains("UX_ProducedLineSsccBarcodes_ActiveSscc", StringComparison.OrdinalIgnoreCase))
            {
                return Error.Conflict(
                    "ProducedLine.SsccAlreadyExists",
                    "One or more SSCC pallet barcodes were concurrently recorded in another active report.");
            }

            return Error.Conflict(
                "ProductionShift.ConcurrentConflict",
                "Another active report for this shift was concurrently recorded.");
        }
    }

    public async Task<Result> RejectShiftReportAsync(
     int reportId,
     string rejectionReason,
     string reviewedBy,
     CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rejectionReason))
        {
            return Error.Validation(
                "ProductionShift.RejectionReasonRequired",
                "Rejection reason is required so the production line can correct the report.");
        }

        if (string.IsNullOrWhiteSpace(reviewedBy))
        {
            return Error.Validation(
                "ProductionShift.ReviewerRequired",
                "Reviewer identifier is required.");
        }

        var currentStatus = await context.ShiftProductionReports
            .Where(r => r.Id == reportId)
            .Select(r => (ShiftReportStatus?)r.Status)
            .FirstOrDefaultAsync(cancellationToken);

        if (currentStatus is null)
        {
            return Error.NotFound(
                "ProductionShift.NotFound",
                $"Shift report #{reportId} was not found.");
        }

        if (currentStatus != ShiftReportStatus.PendingReview)
        {
            return Error.Conflict(
                "ProductionShift.InvalidStatusTransition",
                $"Cannot reject a report in '{currentStatus}' status.");
        }

        var cleanReason = rejectionReason.Trim();
        var cleanReviewer = reviewedBy.Trim();
        var reviewedAt = DateTimeOffset.UtcNow;
        bool updatedSuccessfully = false;

        await context.ExecuteInTransactionAsync(async ct =>
        {
            int affectedReports = await context.ShiftProductionReports
                .Where(r => r.Id == reportId && r.Status == ShiftReportStatus.PendingReview)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, ShiftReportStatus.Rejected)
                    .SetProperty(r => r.RejectionReason, cleanReason)
                    .SetProperty(r => r.ReviewedBy, cleanReviewer)
                    .SetProperty(r => r.ReviewedAtUtc, reviewedAt), ct);

            if (affectedReports == 0)
            {
                return;
            }

            await context.ProducedLineSsccBarcodes
                .Where(b => b.ProducedLine.ShiftProductionReportId == reportId && b.IsActive)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.IsActive, false), ct);

            updatedSuccessfully = true;
        }, cancellationToken);

        if (!updatedSuccessfully)
        {
            return Error.Conflict(
                "ProductionShift.ConcurrentStateChange",
                "The report status was modified concurrently by another request.");
        }

        return Result.Success();
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        if (exception.InnerException is SqlException sqlEx)
        {
            return sqlEx.Number is 2601 or 2627;
        }

        return false;
    }
}