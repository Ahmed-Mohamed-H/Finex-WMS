using System.Diagnostics;
using Finex.Application.Interfaces;
using Finex.Application.Interfaces.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion;

namespace Finex.Application.Services.Catalog;

public sealed class CategoryCatalogCache(
    IFusionCache cache,
    IServiceScopeFactory scopeFactory,
    ILogger<CategoryCatalogCache> logger) : ICategoryCatalogCache, IDisposable
{
    private const string SnapshotCacheKey = "catalog:categories:snapshot:v1";

    // متطابق مع FinexDiagnostics.ServiceName لضمان التقاط السبانز حتى في الخلفية (Eager Refresh)
    private static readonly ActivitySource FallbackActivitySource = new("Finex.WMS.API");

    private static readonly FusionCacheEntryOptions CacheOptions = new FusionCacheEntryOptions()
        .SetDuration(TimeSpan.FromHours(24))
        .SetEagerRefresh(0.8f)
        .SetFailSafe(
            isEnabled: true,
            maxDuration: TimeSpan.FromDays(7),
            throttleDuration: TimeSpan.FromSeconds(30))
        .SetFactoryTimeouts(
            softTimeout: TimeSpan.FromMilliseconds(500),
            hardTimeout: TimeSpan.FromSeconds(5));

    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public async ValueTask<CategoryCatalogSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        using var activity = StartActivity("CategoryCatalogCache.GetSnapshot");
        activity?.SetTag("cache.key", SnapshotCacheKey);

        var dbQueried = false;
        var sw = Stopwatch.StartNew();

        var snapshot = await cache.GetOrSetAsync<CategoryCatalogSnapshot>(
            SnapshotCacheKey,
            async (_, token) =>
            {
                dbQueried = true;
                activity?.AddEvent(new ActivityEvent("Cache miss: querying SQL Server for category catalog snapshot"));
                return await LoadSnapshotFromDatabaseAsync(token);
            },
            CacheOptions,
            token: ct);

        sw.Stop();

        activity?.SetTag("cache.hit", !dbQueried);
        activity?.SetTag("catalog.cached_categories_count", snapshot.ById.Count);

        if (dbQueried)
        {
            logger.LogInformation(
                "Loaded category catalog snapshot ({CategoryCount} categories) into FusionCache under {CacheKey} in {ElapsedMs} ms.",
                snapshot.ById.Count,
                SnapshotCacheKey,
                sw.ElapsedMilliseconds);
        }
        else
        {
            logger.LogDebug(
                "Category catalog cache hit for {CacheKey} ({CategoryCount} categories) in {ElapsedMs:F3} ms.",
                SnapshotCacheKey,
                snapshot.ById.Count,
                sw.Elapsed.TotalMilliseconds);
        }

        return snapshot;
    }

    public async Task RefreshProactivelyAsync(CancellationToken ct = default)
    {
        using var activity = StartActivity("CategoryCatalogCache.RefreshProactively");
        activity?.SetTag("cache.key", SnapshotCacheKey);

        var sw = Stopwatch.StartNew();

        await _refreshLock.WaitAsync(ct);
        try
        {
            activity?.AddEvent(new ActivityEvent("Evicting stale category catalog snapshot from cache"));
            await cache.RemoveAsync(SnapshotCacheKey, token: ct);

            var freshSnapshot = await LoadSnapshotFromDatabaseAsync(ct);
            await cache.SetAsync(SnapshotCacheKey, freshSnapshot, CacheOptions, token: ct);

            sw.Stop();

            activity?.SetTag("catalog.cached_categories_count", freshSnapshot.ById.Count);
            activity?.SetTag("cache.refresh_duration_ms", sw.ElapsedMilliseconds);

            logger.LogInformation(
                "Proactively refreshed category catalog cache {CacheKey} with {CategoryCount} categories in {ElapsedMs} ms.",
                SnapshotCacheKey,
                freshSnapshot.ById.Count,
                sw.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            activity?.AddEvent(new ActivityEvent("Proactive cache refresh failed; cache evicted for lazy reload"));

            logger.LogWarning(
                ex,
                "Proactive category cache refresh failed for {CacheKey} after {ElapsedMs} ms; cache evicted and will lazy-reload on next read.",
                SnapshotCacheKey,
                sw.ElapsedMilliseconds);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<CategoryCatalogSnapshot> LoadSnapshotFromDatabaseAsync(CancellationToken ct)
    {
        using var activity = StartActivity("CategoryCatalogCache.LoadSnapshotFromDatabase");
        var sw = Stopwatch.StartNew();

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IFinexDbContext>();

        var nodes = await dbContext.ProductCategories
            .AsNoTracking()
            .OrderBy(c => c.Level)
            .ThenBy(c => c.Name)
            .Select(c => new CategorySnapshotNode(
                c.Id,
                c.Code,
                c.Name,
                c.Description,
                c.Level,
                c.IsActive,
                c.ParentCategoryId,
                c.AttributeSchema))
            .ToListAsync(ct);

        var snapshot = CategoryCatalogSnapshot.Create(nodes);
        sw.Stop();

        activity?.SetTag("catalog.loaded_nodes_count", nodes.Count);
        activity?.SetTag("db.query_duration_ms", sw.ElapsedMilliseconds);

        return snapshot;
    }

    private static Activity? StartActivity(string operationName)
        => Activity.Current?.Source.StartActivity(operationName, ActivityKind.Internal)
           ?? FallbackActivitySource.StartActivity(operationName, ActivityKind.Internal);

    public void Dispose()
    {
        _refreshLock.Dispose();
    }
}