using Finex.Application.Common.Results;
using Finex.Application.DTOs.Catalog;
using Finex.Application.Interfaces;
using Finex.Application.Interfaces.Catalog;
using Finex.Application.Mappings.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using ZiggyCreatures.Caching.Fusion;

namespace Finex.Application.Services.Catalog;

public sealed class CatalogService(
    IFinexDbContext dbContext,
    IFusionCache cache,
    ILogger<CatalogService> logger) : ICatalogService
{
    private const string CatalogTreeTag = "catalog:tree";
    private const string ActiveTreeCacheKey = "catalog:tree:active";
    private const string AllTreeCacheKey = "catalog:tree:all";
    private const int MaxTreeDepth = 20;
    private const int CteRecursionGuard = 100;

    private static readonly ActivitySource ActivitySource = new("Finex.Application.Catalog");

    public async Task<Result<CategoryDetailsDto>> CreateCategoryAsync(
        CreateCategoryDto dto,
        CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity(nameof(CreateCategoryAsync));

        var normalizedCode = dto.Code.Trim().ToUpperInvariant();
        var normalizedName = dto.Name.Trim();

        var codeExists = await dbContext.ProductCategories
            .AsNoTracking()
            .AnyAsync(c => c.Code == normalizedCode, ct);

        if (codeExists)
        {
            logger.LogWarning("Category creation rejected due to duplicate code {CategoryCode}", normalizedCode);
            return Error.Conflict(
                "Catalog.DuplicateConflict",
                "A category with this code or name already exists.");
        }

        var level = 1;
        string? parentCode = null;
        string? parentName = null;

        if (dto.ParentCategoryId.HasValue)
        {
            var parent = await dbContext.ProductCategories
                .AsNoTracking()
                .Where(c => c.Id == dto.ParentCategoryId.Value)
                .Select(c => new { c.Id, c.Code, c.Name, c.Level, c.IsActive })
                .FirstOrDefaultAsync(ct);

            if (parent is null || !parent.IsActive)
            {
                logger.LogWarning(
                    "Category creation rejected for code {CategoryCode}: target parent {ParentId} is missing or inactive",
                    normalizedCode,
                    dto.ParentCategoryId.Value);

                return Error.Conflict(
                    "Catalog.InvalidHierarchy",
                    "The category cannot be assigned under the specified parent.");
            }

            if (parent.Level + 1 > MaxTreeDepth)
            {
                logger.LogWarning(
                    "Category creation rejected for code {CategoryCode}: hierarchy depth limit exceeded under parent {ParentId}",
                    normalizedCode,
                    parent.Id);

                return Error.Validation(
                    "Catalog.HierarchyRuleViolation",
                    "The requested category hierarchy exceeds the allowed depth limit.");
            }

            level = parent.Level + 1;
            parentCode = parent.Code;
            parentName = parent.Name;
        }

        var nameExistsAtSameLevel = await dbContext.ProductCategories
            .AsNoTracking()
            .AnyAsync(c => c.ParentCategoryId == dto.ParentCategoryId && c.Name == normalizedName, ct);

        if (nameExistsAtSameLevel)
        {
            logger.LogWarning(
                "Category creation rejected due to duplicate name under parent {ParentId}",
                dto.ParentCategoryId);

            return Error.Conflict(
                "Catalog.DuplicateConflict",
                "A category with this code or name already exists.");
        }

        var entity = dto.ToEntity();
        entity.Code = normalizedCode;
        entity.Name = normalizedName;
        entity.Level = level;
        entity.IsActive = true;
        entity.CreatedAtUtc = DateTimeOffset.UtcNow;

        dbContext.ProductCategories.Add(entity);

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(
                ex,
                "Database constraint conflict while creating category {CategoryCode}",
                normalizedCode);

            return Error.Conflict(
                "Catalog.DuplicateConflict",
                "A category with this code or name already exists.");
        }

        await InvalidateCatalogCacheAsync(ct);

        logger.LogInformation(
            "Created product category {CategoryId} ({CategoryCode}) at Level {Level}",
            entity.Id,
            entity.Code,
            entity.Level);

        return new CategoryDetailsDto(
            entity.Id,
            entity.Code,
            entity.Name,
            entity.Description,
            entity.Level,
            entity.IsActive,
            entity.ParentCategoryId,
            parentCode,
            parentName,
            SubCategoriesCount: 0,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);
    }

    public async Task<Result<IReadOnlyList<CategoryTreeNodeDto>>> GetCategoryTreeAsync(
        bool includeInactive = false,
        CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity(nameof(GetCategoryTreeAsync));
        activity?.SetTag("catalog.include_inactive", includeInactive);

        var cacheKey = includeInactive ? AllTreeCacheKey : ActiveTreeCacheKey;

        var tree = await cache.GetOrSetAsync<IReadOnlyList<CategoryTreeNodeDto>>(
            cacheKey,
            async (ctx, token) =>
            {
                var query = dbContext.ProductCategories.AsNoTracking();

                if (!includeInactive)
                {
                    query = query.Where(c => c.IsActive);
                }

                var flatNodes = await query
                    .OrderBy(c => c.Level)
                    .ThenBy(c => c.Name)
                    .ProjectToFlatNodes()
                    .ToListAsync(token);

                return BuildTreeInMemory(flatNodes);
            },
            options => options
                .SetDuration(TimeSpan.FromMinutes(10))
                .SetFailSafe(true, TimeSpan.FromHours(1))
                .SetFactoryTimeouts(TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(5)),
            tags: [CatalogTreeTag],
            token: ct);

        return Result<IReadOnlyList<CategoryTreeNodeDto>>.Success(tree);
    }

    public async Task<Result<CategoryDetailsDto>> GetCategoryByIdAsync(
        int id,
        CancellationToken ct = default)
    {
        var dto = await dbContext.ProductCategories
            .AsNoTracking()
            .Where(c => c.Id == id)
            .ProjectToDetailsDto()
            .FirstOrDefaultAsync(ct);

        if (dto is null)
        {
            return Error.NotFound(
                "Catalog.CategoryNotFound",
                "The requested category was not found.");
        }

        return dto;
    }

    public async Task<Result<CategoryDetailsDto>> UpdateCategoryAsync(
        int id,
        UpdateCategoryDto dto,
        CancellationToken ct = default)
    {
        var category = await dbContext.ProductCategories
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (category is null)
        {
            return Error.NotFound(
                "Catalog.CategoryNotFound",
                "The requested category was not found.");
        }

        var normalizedName = dto.Name.Trim();

        var duplicateName = await dbContext.ProductCategories
            .AsNoTracking()
            .AnyAsync(c =>
                c.Id != id &&
                c.ParentCategoryId == category.ParentCategoryId &&
                c.Name == normalizedName, ct);

        if (duplicateName)
        {
            logger.LogWarning(
                "Category update rejected for {CategoryId} due to duplicate name under parent {ParentId}",
                id,
                category.ParentCategoryId);

            return Error.Conflict(
                "Catalog.DuplicateConflict",
                "A category with this code or name already exists.");
        }

        category.Name = normalizedName;
        category.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        category.UpdatedAtUtc = DateTimeOffset.UtcNow;

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Database conflict while updating category {CategoryId}", id);
            return Error.Conflict(
                "Catalog.DuplicateConflict",
                "A category with this code or name already exists.");
        }

        await InvalidateCatalogCacheAsync(ct);

        logger.LogInformation("Updated product category {CategoryId} ({CategoryCode})", category.Id, category.Code);

        var updatedDto = await dbContext.ProductCategories
            .AsNoTracking()
            .Where(c => c.Id == id)
            .ProjectToDetailsDto()
            .FirstOrDefaultAsync(ct);

        return updatedDto ?? new CategoryDetailsDto(
            category.Id,
            category.Code,
            category.Name,
            category.Description,
            category.Level,
            category.IsActive,
            category.ParentCategoryId,
            ParentCategoryCode: null,
            ParentCategoryName: null,
            SubCategoriesCount: 0,
            category.CreatedAtUtc,
            category.UpdatedAtUtc);
    }

    public async Task<Result<CategoryDetailsDto>> MoveCategoryAsync(
        int id,
        MoveCategoryDto dto,
        CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity(nameof(MoveCategoryAsync));

        var newParentCategoryId = dto.NewParentCategoryId;

        if (newParentCategoryId == id)
        {
            logger.LogWarning("Category move rejected for {CategoryId}: self-parent assignment attempted", id);
            return Error.Conflict(
                "Catalog.InvalidHierarchy",
                "The category cannot be moved to the specified parent.");
        }

        var newLevel = 1;
        string? newParentCode = null;
        string? newParentName = null;

        if (newParentCategoryId.HasValue)
        {
            var ancestors = await dbContext.GetCategoryAncestorsAsync(
                newParentCategoryId.Value,
                CteRecursionGuard,
                ct);

            var newParent = ancestors.FirstOrDefault(a => a.Id == newParentCategoryId.Value);

            if (newParent is null || !newParent.IsActive)
            {
                logger.LogWarning(
                    "Category move rejected for {CategoryId}: target parent {NewParentId} is missing or inactive",
                    id,
                    newParentCategoryId.Value);

                return Error.Conflict(
                    "Catalog.InvalidHierarchy",
                    "The category cannot be moved to the specified parent.");
            }

            if (ancestors.Any(a => a.Id == id))
            {
                logger.LogWarning(
                    "Category move rejected for {CategoryId}: circular hierarchy detected via parent {NewParentId}",
                    id,
                    newParentCategoryId.Value);

                return Error.Conflict(
                    "Catalog.InvalidHierarchy",
                    "The category cannot be moved to the specified parent.");
            }

            newLevel = newParent.Level + 1;
            newParentCode = newParent.Code;
            newParentName = newParent.Name;
        }

        CategoryDetailsDto? movedDetails = null;
        Error? domainError = null;

        await dbContext.ExecuteInTransactionAsync(async token =>
        {
            var subtreeEntities = await dbContext.GetCategorySubtreeAsync(
                id,
                CteRecursionGuard,
                token);

            var targetEntity = subtreeEntities.FirstOrDefault(c => c.Id == id);
            if (targetEntity is null)
            {
                domainError = Error.NotFound(
                    "Catalog.CategoryNotFound",
                    "The requested category was not found.");
                return;
            }

            var directChildrenCount = subtreeEntities.Count(c => c.ParentCategoryId == id);

            if (targetEntity.ParentCategoryId == newParentCategoryId)
            {
                movedDetails = new CategoryDetailsDto(
                    targetEntity.Id,
                    targetEntity.Code,
                    targetEntity.Name,
                    targetEntity.Description,
                    targetEntity.Level,
                    targetEntity.IsActive,
                    targetEntity.ParentCategoryId,
                    newParentCode,
                    newParentName,
                    directChildrenCount,
                    targetEntity.CreatedAtUtc,
                    targetEntity.UpdatedAtUtc);
                return;
            }

            var nameConflict = await dbContext.ProductCategories
                .AsNoTracking()
                .AnyAsync(c =>
                    c.Id != id &&
                    c.ParentCategoryId == newParentCategoryId &&
                    c.Name == targetEntity.Name, token);

            if (nameConflict)
            {
                logger.LogWarning(
                    "Category move rejected for {CategoryId}: duplicate name under target parent {NewParentId}",
                    id,
                    newParentCategoryId);

                domainError = Error.Conflict(
                    "Catalog.DuplicateConflict",
                    "A category with this code or name already exists.");
                return;
            }

            var levelDelta = newLevel - targetEntity.Level;
            if (levelDelta > 0)
            {
                var maxSubtreeLevel = subtreeEntities.Max(c => c.Level);
                if (maxSubtreeLevel + levelDelta > MaxTreeDepth)
                {
                    logger.LogWarning(
                        "Category move rejected for {CategoryId}: subtree depth would exceed limit",
                        id);

                    domainError = Error.Validation(
                        "Catalog.HierarchyRuleViolation",
                        "The requested category hierarchy exceeds the allowed depth limit.");
                    return;
                }
            }

            var now = DateTimeOffset.UtcNow;
            targetEntity.ParentCategoryId = newParentCategoryId;
            targetEntity.Level = newLevel;
            targetEntity.UpdatedAtUtc = now;

            if (levelDelta != 0)
            {
                foreach (var descendant in subtreeEntities.Where(c => c.Id != id))
                {
                    descendant.Level += levelDelta;
                    descendant.UpdatedAtUtc = now;
                }
            }

            try
            {
                await dbContext.SaveChangesAsync(token);
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(ex, "Database constraint conflict while moving category {CategoryId}", id);
                domainError = Error.Conflict(
                    "Catalog.DuplicateConflict",
                    "A category with this code or name already exists.");
                return;
            }

            movedDetails = new CategoryDetailsDto(
                targetEntity.Id,
                targetEntity.Code,
                targetEntity.Name,
                targetEntity.Description,
                targetEntity.Level,
                targetEntity.IsActive,
                targetEntity.ParentCategoryId,
                newParentCode,
                newParentName,
                directChildrenCount,
                targetEntity.CreatedAtUtc,
                targetEntity.UpdatedAtUtc);
        }, ct);

        if (domainError is not null)
        {
            return domainError;
        }

        await InvalidateCatalogCacheAsync(ct);

        logger.LogInformation(
            "Moved category {CategoryId} ({CategoryCode}) to parent {NewParentId} at Level {NewLevel}",
            id,
            movedDetails!.Code,
            newParentCategoryId,
            newLevel);

        return movedDetails;
    }

    public async Task<Result> DeactivateCategoryAsync(int id, CancellationToken ct = default)
    {
        var category = await dbContext.ProductCategories
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (category is null)
        {
            return Error.NotFound(
                "Catalog.CategoryNotFound",
                "The requested category was not found.");
        }

        if (!category.IsActive)
        {
            return Result.Success();
        }

        var hasActiveChildren = await dbContext.ProductCategories
            .AsNoTracking()
            .AnyAsync(c => c.ParentCategoryId == id && c.IsActive, ct);

        if (hasActiveChildren)
        {
            logger.LogWarning(
                "Deactivation blocked for category {CategoryId} ({CategoryCode}): active sub-categories exist",
                category.Id,
                category.Code);

            return Error.Conflict(
                "Catalog.InvalidHierarchy",
                "Cannot deactivate this category while it still has active sub-categories.");
        }

        category.IsActive = false;
        category.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(ct);
        await InvalidateCatalogCacheAsync(ct);

        logger.LogInformation("Deactivated product category {CategoryId} ({CategoryCode})", category.Id, category.Code);

        return Result.Success();
    }

    public async Task<Result> ActivateCategoryAsync(int id, CancellationToken ct = default)
    {
        var category = await dbContext.ProductCategories
            .Include(c => c.ParentCategory)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (category is null)
        {
            return Error.NotFound(
                "Catalog.CategoryNotFound",
                "The requested category was not found.");
        }

        if (category.IsActive)
        {
            return Result.Success();
        }

        if (category.ParentCategory is not null && !category.ParentCategory.IsActive)
        {
            logger.LogWarning(
                "Activation blocked for category {CategoryId} ({CategoryCode}): parent {ParentId} is inactive",
                category.Id,
                category.Code,
                category.ParentCategory.Id);

            return Error.Conflict(
                "Catalog.InvalidHierarchy",
                "Cannot assign or activate under an inactive parent category.");
        }

        category.IsActive = true;
        category.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(ct);
        await InvalidateCatalogCacheAsync(ct);

        logger.LogInformation("Activated product category {CategoryId} ({CategoryCode})", category.Id, category.Code);

        return Result.Success();
    }

    private static IReadOnlyList<CategoryTreeNodeDto> BuildTreeInMemory(
        IReadOnlyList<CategoryFlatNodeDto> flatNodes)
    {
        if (flatNodes.Count == 0)
        {
            return [];
        }

        var builders = new Dictionary<int, MutableTreeNode>(flatNodes.Count);
        var roots = new List<MutableTreeNode>();

        foreach (var node in flatNodes)
        {
            builders[node.Id] = new MutableTreeNode(node);
        }

        foreach (var node in flatNodes)
        {
            var current = builders[node.Id];

            if (node.ParentCategoryId.HasValue &&
                builders.TryGetValue(node.ParentCategoryId.Value, out var parentBuilder))
            {
                parentBuilder.Children.Add(current);
            }
            else
            {
                roots.Add(current);
            }
        }

        return roots.Select(ToImmutableDto).ToList();
    }

    private static CategoryTreeNodeDto ToImmutableDto(MutableTreeNode node)
    {
        var children = node.Children.Count == 0
            ? (IReadOnlyList<CategoryTreeNodeDto>)[]
            : node.Children.Select(ToImmutableDto).ToList();

        return new CategoryTreeNodeDto(
            node.Data.Id,
            node.Data.Code,
            node.Data.Name,
            node.Data.Description,
            node.Data.Level,
            node.Data.IsActive,
            node.Data.ParentCategoryId,
            children);
    }

    private async Task InvalidateCatalogCacheAsync(CancellationToken ct)
    {
        await cache.RemoveByTagAsync(CatalogTreeTag, token: ct);
    }

    private sealed class MutableTreeNode(CategoryFlatNodeDto data)
    {
        public CategoryFlatNodeDto Data { get; } = data;
        public List<MutableTreeNode> Children { get; } = [];
    }
}