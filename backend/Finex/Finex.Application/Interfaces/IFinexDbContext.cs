using Finex.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Finex.Application.Interfaces
{
    public interface IFinexDbContext
    {
        DbSet<ShiftProductionReport> ShiftProductionReports { get; }
        DbSet<ProducedLine> ProducedLines { get; }
        DbSet<ProducedLineSsccBarcode> ProducedLineSsccBarcodes { get; }

        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
