using Finex.Domain.Enums;

namespace Finex.Application.DTOs.Production
{
    public sealed record ShiftProductionReportDetailsDto(
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
     IReadOnlyList<ProducedLineDetailsDto> ProducedLines);
}