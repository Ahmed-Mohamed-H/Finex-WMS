using Finex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Finex.Application.Interfaces;

public interface IFinexDbContext
{
    DbSet<ShiftProductionReport> ShiftProductionReports { get; }
    DbSet<ProducedLine> ProducedLines { get; }
    DbSet<ProducedLineSsccBarcode> ProducedLineSsccBarcodes { get; }
    DbSet<ProductCategory> ProductCategories { get; }
    DbSet<Product> Products { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default);

    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken = default);

    Task<List<ProductCategory>> GetCategoryAncestorsAsync(
    int categoryId,
    int maxDepth,
    CancellationToken ct = default);

    Task<List<ProductCategory>> GetCategorySubtreeAsync(
        int rootCategoryId,
        int maxDepth,
        CancellationToken ct = default);
}