using Finex.Application.Common.Results;
using Finex.Application.DTOs;
using Finex.Application.Interfaces;
using Finex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Finex.Application.Services;

public sealed class ProductionShiftService(IFinexDbContext context) : IProductionShiftService
{
    public async Task<Result<int>> IngestShiftReportAsync(
        ShiftProductionReportDto dto,
        CancellationToken cancellationToken = default)
    {
        var uniqueBarcodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in dto.ProducedLines)
        {
            foreach (var rawBarcode in line.SsccBarcodes)
            {
                var barcode = rawBarcode.Trim();
                if (!uniqueBarcodes.Add(barcode))
                {
                    return Error.Conflict(
                        "ProducedLine.DuplicateSsccInReport",
                        $"Pallet barcode {barcode} is duplicated within the same report.");
                }
            }
        }

        var plantCode = dto.PlantCode.Trim();
        var lineCode = dto.LineCode.Trim();
        var shiftCode = dto.ShiftCode.Trim();

        bool shiftExists = await context.ShiftProductionReports
            .AnyAsync(x => x.PlantCode == plantCode
                        && x.LineCode == lineCode
                        && x.ProductionDate == dto.ProductionDate
                        && x.ShiftCode == shiftCode, cancellationToken);

        if (shiftExists)
        {
            return Error.Conflict(
                "ProductionShift.AlreadyExists",
                "A report for this shift is already recorded in the system.");
        }

        if (uniqueBarcodes.Count > 0)
        {
            var existingBarcode = await context.ProducedLineSsccBarcodes
                .Where(x => uniqueBarcodes.Contains(x.SsccBarcode))
                .Select(x => x.SsccBarcode)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingBarcode is not null)
            {
                return Error.Conflict(
                    "ProducedLine.SsccAlreadyExists",
                    $"Pallet barcode {existingBarcode} is already recorded in the system.");
            }
        }

        var reportEntity = new ShiftProductionReport
        {
            PlantCode = plantCode,
            LineCode = lineCode,
            ShiftCode = shiftCode,
            ProductionDate = dto.ProductionDate,
            ShiftStartedAt = dto.ShiftStartedAt,
            ShiftEndedAt = dto.ShiftEndedAt,
            ShiftSupervisorCode = dto.ShiftSupervisorCode.Trim(),
            ProducedLines = dto.ProducedLines.Select(line => new ProducedLine
            {
                Sku = line.Sku.Trim(),
                UnitName = line.UnitName.Trim(),
                UnitBarcode = line.UnitBarcode.Trim(),
                Quantity = line.Quantity,
                ProductionDate = line.ProductionDate,
                ExpiryDate = line.ExpiryDate,
                SsccBarcodes = line.SsccBarcodes.Select(code => new ProducedLineSsccBarcode
                {
                    SsccBarcode = code.Trim()
                }).ToList()
            }).ToList()
        };

        context.ShiftProductionReports.Add(reportEntity);
        await context.SaveChangesAsync(cancellationToken);

        return reportEntity.Id;
    }
}