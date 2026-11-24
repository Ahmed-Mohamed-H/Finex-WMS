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
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FinexDbContext).Assembly);
    }

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

    public Task<List<ProductCategory>> GetCategoryAncestorsAsync(
     int categoryId,
     int maxDepth,
     CancellationToken ct = default)
    {
        return ProductCategories
            .FromSqlInterpolated($"""
            WITH Ancestors AS (
                SELECT Id, ParentCategoryId, 1 AS Depth
                FROM ProductCategories
                WHERE Id = {categoryId}
                UNION ALL
                SELECT p.Id, p.ParentCategoryId, a.Depth + 1
                FROM ProductCategories p
                INNER JOIN Ancestors a ON p.Id = a.ParentCategoryId
                WHERE a.Depth < {maxDepth}
            )
            SELECT p.*
            FROM ProductCategories p
            INNER JOIN Ancestors a ON p.Id = a.Id
            """)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public Task<List<ProductCategory>> GetCategorySubtreeAsync(
        int rootCategoryId,
        int maxDepth,
        CancellationToken ct = default)
    {
        return ProductCategories
            .FromSqlInterpolated($"""
            WITH Subtree AS (
                SELECT Id, ParentCategoryId, 1 AS Depth
                FROM ProductCategories
                WHERE Id = {rootCategoryId}
                UNION ALL
                SELECT c.Id, c.ParentCategoryId, s.Depth + 1
                FROM ProductCategories c
                INNER JOIN Subtree s ON c.ParentCategoryId = s.Id
                WHERE s.Depth < {maxDepth}
            )
            SELECT c.*
            FROM ProductCategories c
            INNER JOIN Subtree s ON c.Id = s.Id
            """)
            .ToListAsync(ct);
    }
}