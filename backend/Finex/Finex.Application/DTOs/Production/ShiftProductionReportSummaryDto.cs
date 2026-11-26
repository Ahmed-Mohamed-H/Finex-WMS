using Finex.Domain.Enums.Production;

namespace Finex.Application.DTOs.Production
{
    public sealed record ShiftProductionReportSummaryDto(
    int Id,
    string PlantCode,
    string LineCode,
    string ShiftCode,
    DateOnly ProductionDate,
    DateTimeOffset ShiftStartedAt,
    DateTimeOffset ShiftEndedAt,
    string ShiftSupervisorCode,
    ShiftReportStatus Status,
    int RevisionNumber,
    int? SupersededByReportId,
    string? ReviewedBy,
    DateTimeOffset? ReviewedAtUtc,
    string? RejectionReason,
    IReadOnlyList<ProducedLineSummaryDto> ProducedLines);
}