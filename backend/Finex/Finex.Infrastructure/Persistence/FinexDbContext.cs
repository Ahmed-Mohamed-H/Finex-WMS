using Finex.Application.Interfaces;
using Finex.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Finex.Infrastructure.Persistence
{
    public sealed class FinexDbContext : DbContext, IFinexDbContext
    {
        public FinexDbContext(DbContextOptions<FinexDbContext> options)
            : base(options)
        {
        }

        public DbSet<ShiftProductionReport> ShiftProductionReports => Set<ShiftProductionReport>();
        public DbSet<ProducedLine> ProducedLines => Set<ProducedLine>();
        public DbSet<ProducedLineSsccBarcode> ProducedLineSsccBarcodes => Set<ProducedLineSsccBarcode>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.ApplyConfigurationsFromAssembly(typeof(FinexDbContext).Assembly);
        }
    }
}
