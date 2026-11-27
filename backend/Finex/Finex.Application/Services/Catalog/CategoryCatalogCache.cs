using Finex.Application.Interfaces;
using Finex.Application.Interfaces.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using ZiggyCreatures.Caching.Fusion;

namespace Finex.Application.Services.Catalog
{
    public sealed class CategoryCatalogCache(
     IFusionCache cache,
     IServiceScopeFactory scopeFactory,
     ILogger<CategoryCatalogCache> logger) : ICategoryCatalogCache, IDisposable
    {
        private const string SnapshotCacheKey = "catalog:categories:snapshot:v1";

        private static readonly ActivitySource ActivitySource = new("Finex.Application.Catalog.Cache");

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

        public ValueTask<CategoryCatalogSnapshot> GetSnapshotAsync(CancellationToken ct = default)
        {
            return cache.GetOrSetAsync<CategoryCatalogSnapshot>(
                SnapshotCacheKey,
                async (_, token) => await LoadSnapshotFromDatabaseAsync(token),
                CacheOptions,
                token: ct);
        }

        public async Task RefreshProactivelyAsync(CancellationToken ct = default)
        {
            using var activity = ActivitySource.StartActivity(nameof(RefreshProactivelyAsync));

            await _refreshLock.WaitAsync(ct);
            try
            {
                await cache.RemoveAsync(SnapshotCacheKey, token: ct);

                var freshSnapshot = await LoadSnapshotFromDatabaseAsync(ct);
                await cache.SetAsync(SnapshotCacheKey, freshSnapshot, CacheOptions, token: ct);

                logger.LogInformation(
                    "Proactively refreshed category catalog cache with {CategoryCount} categories",
                    freshSnapshot.ById.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(
                    ex,
                    "Proactive category cache refresh failed; cache evicted and will lazy-reload on next read");
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        private async Task<CategoryCatalogSnapshot> LoadSnapshotFromDatabaseAsync(CancellationToken ct)
        {
            using var activity = ActivitySource.StartActivity(nameof(LoadSnapshotFromDatabaseAsync));

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

            return CategoryCatalogSnapshot.Create(nodes);
        }

        public void Dispose()
        {
            _refreshLock.Dispose();
        }
    }
}
