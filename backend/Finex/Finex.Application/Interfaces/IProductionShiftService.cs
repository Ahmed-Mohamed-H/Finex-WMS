using Finex.Application.Common.Results;
using Finex.Application.DTOs;

namespace Finex.Application.Interfaces;

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
}