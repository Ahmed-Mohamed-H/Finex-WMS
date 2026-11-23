using Finex.Domain.Enums;

namespace Finex.Domain.Entities;

public sealed class ShiftProductionReport
{
    public int Id { get; set; }
    public string PlantCode { get; set; } = string.Empty;
    public string LineCode { get; set; } = string.Empty;
    public string ShiftCode { get; set; } = string.Empty;
    public DateOnly ProductionDate { get; set; }
    public DateTimeOffset ShiftStartedAt { get; set; }
    public DateTimeOffset ShiftEndedAt { get; set; }
    public string ShiftSupervisorCode { get; set; } = string.Empty;

    public ShiftReportStatus Status { get; set; } = ShiftReportStatus.PendingReview;
    public int RevisionNumber { get; set; } = 1;
    public string? RejectionReason { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAtUtc { get; set; }

    public int? SupersededByReportId { get; set; }
    public ShiftProductionReport? SupersededByReport { get; set; }

    public ICollection<ProducedLine> ProducedLines { get; set; } = [];
}