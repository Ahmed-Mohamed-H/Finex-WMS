using Finex.Application.DTOs;
using Finex.Domain.Entities;
using Finex.Domain.Enums;
using Riok.Mapperly.Abstractions;

namespace Finex.Application.Mappings;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Source)]
public static partial class ProductionShiftMapper
{
    [MapperRequiredMapping(RequiredMappingStrategy.Target)]
    public static partial IQueryable<ShiftProductionReportSummaryDto> ProjectToSummaryDto(
        this IQueryable<ShiftProductionReport> query);

    [MapperRequiredMapping(RequiredMappingStrategy.Target)]
    public static partial IQueryable<ShiftProductionReportDetailsDto> ProjectToDetailsDto(
        this IQueryable<ShiftProductionReport> query);

    [MapValue(nameof(ShiftProductionReport.Status), ShiftReportStatus.PendingReview)]
    public static partial ShiftProductionReport ToEntity(this ShiftProductionReportDto dto);

    private static partial ProducedLine MapProducedLine(ProducedLineDto dto);

    private static ProducedLineSsccBarcode MapSsccBarcode(string sscc) => new()
    {
        SsccBarcode = sscc.Trim(),
        IsActive = true
    };

    private static string TrimString(string value) => value.Trim();
}