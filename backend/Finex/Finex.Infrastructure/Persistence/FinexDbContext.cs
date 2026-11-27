using Finex.Application.Interfaces;
using Finex.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Finex.Infrastructure.Persistence;

public class FinexDbContext(DbContextOptions<FinexDbContext> options)
    : DbContext(options), IFinexDbContext
{
    public DbSet<ShiftProductionReport> ShiftProductionReports => Set<ShiftProductionReport>();
    public DbSet<ProducedLine> ProducedLines => Set<ProducedLine>();
    public DbSet<ProducedLineSsccBarcode> ProducedLineSsccBarcodes => Set<ProducedLineSsccBarcode>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<Product> Products => Set<Product>();
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

    public async Task<int> CountProductsWithAttributeKeyAsync(
    IReadOnlyCollection<int> categoryIds,
    string attributeKey,
    CancellationToken ct = default)
    {
        if (categoryIds.Count == 0)
            return 0;

        var categoryIdsJson = JsonSerializer.Serialize(categoryIds);
        var jsonPath = $"$.{attributeKey}";

        var sql = """
        SELECT COUNT(*) AS [Value]
        FROM [Products] AS p
        WHERE p.[CategoryId] IN (
            SELECT CAST([value] AS int) FROM OPENJSON(@categoryIdsJson)
        )
        AND JSON_PATH_EXISTS(p.[TechnicalSpecs], @jsonPath) = 1
        """;

        return await Database
            .SqlQueryRaw<int>(
                sql,
                new SqlParameter("@categoryIdsJson", categoryIdsJson),
                new SqlParameter("@jsonPath", jsonPath))
            .SingleAsync(ct);
    }

    public async Task<int> CountProductsWithAttributeValueAsync(
        IReadOnlyCollection<int> categoryIds,
        string attributeKey,
        string attributeValue,
        CancellationToken ct = default)
    {
        if (categoryIds.Count == 0)
            return 0;

        var categoryIdsJson = JsonSerializer.Serialize(categoryIds);
        var jsonPath = $"$.{attributeKey}";

        var sql = """
        SELECT COUNT(*) AS [Value]
        FROM [Products] AS p
        WHERE p.[CategoryId] IN (
            SELECT CAST([value] AS int) FROM OPENJSON(@categoryIdsJson)
        )
        AND JSON_VALUE(p.[TechnicalSpecs], @jsonPath) = @attributeValue
        """;

        return await Database
            .SqlQueryRaw<int>(
                sql,
                new SqlParameter("@categoryIdsJson", categoryIdsJson),
                new SqlParameter("@jsonPath", jsonPath),
                new SqlParameter("@attributeValue", attributeValue))
            .SingleAsync(ct);
    }
}