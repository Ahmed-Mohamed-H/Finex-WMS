using Finex.Application.Common.Results;
using Finex.Application.DTOs;
using System;
using System.Collections.Generic;
using System.Text;

namespace Finex.Application.Interfaces
{
    public interface IProductionShiftService
    {
        Task<Result<int>> IngestShiftReportAsync(
            ShiftProductionReportDto dto,
            CancellationToken cancellationToken = default);
    }
}
