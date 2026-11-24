using Finex.Application.Interfaces;
using Finex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Finex.Infrastructure.Persistence;

public class FinexDbContext(DbContextOptions<FinexDbContext> options)
    : DbContext(options), IFinexDbContext
{
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
            ChangeTracker.Clear();
            await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
            await action(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        var strategy = Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            ChangeTracker.Clear();

            await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
            var result = await action(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return result;
        });
    }
}