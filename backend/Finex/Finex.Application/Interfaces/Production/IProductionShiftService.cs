using Finex.Application.Common.Models;
using Finex.Application.Common.Results;
using Finex.Application.DTOs.Production;
using Finex.Domain.Enums.Production;

namespace Finex.Application.Interfaces.Production;

public interface IProductionShiftService
{
    Task<Result<int>> IngestShiftReportAsync(
        ShiftProductionReportDto dto,
        CancellationToken cancellationToken = default);

    Task<Result> RejectShiftReportAsync(
        int reportId,
        string rejectionReason,
        string reviewedBy,
        CancellationToken cancellationToken = default);

    Task<Result<PagedData<ShiftProductionReportSummaryDto>>> GetShiftReportsAsync(
        ShiftReportStatus? status = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    Task<Result<ShiftProductionReportDetailsDto>> GetShiftReportByIdAsync(
        int reportId,
        CancellationToken cancellationToken = default);

    Task<Result> ApproveShiftReportAsync(
        int reportId,
        string reviewedBy,
        CancellationToken cancellationToken = default);
}