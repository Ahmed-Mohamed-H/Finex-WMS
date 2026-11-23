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

        public async Task ExecuteInTransactionAsync(
            Func<CancellationToken, Task> action,
            CancellationToken cancellationToken = default)
        {
            var strategy = Database.CreateExecutionStrategy();

            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    await action(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
            });
        }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.ApplyConfigurationsFromAssembly(typeof(FinexDbContext).Assembly);
        }
    }
}
