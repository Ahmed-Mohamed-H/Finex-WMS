using Finex.Application.DTOs;
using Finex.Domain.Entities;
using Riok.Mapperly.Abstractions;

namespace Finex.Application.Mappings;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class ProductionShiftQueryMapper
{
    public static partial IQueryable<ShiftProductionReportSummaryDto> ProjectToSummaryDto(
        this IQueryable<ShiftProductionReport> query);

    public static partial IQueryable<ShiftProductionReportDetailsDto> ProjectToDetailsDto(
        this IQueryable<ShiftProductionReport> query);
}