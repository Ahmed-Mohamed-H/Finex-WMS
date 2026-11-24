using Finex.Application.DTOs.Production;
using Finex.Domain.Entities;
using Finex.Domain.Enums;
using Riok.Mapperly.Abstractions;

namespace Finex.Application.Mappings;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Source)]
public static partial class ProductionShiftCommandMapper
{
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