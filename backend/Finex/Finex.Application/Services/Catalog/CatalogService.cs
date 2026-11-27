using System.Diagnostics;
using Finex.Application.Common.Results;
using Finex.Application.DTOs.Catalog;
using Finex.Application.Interfaces;
using Finex.Application.Interfaces.Catalog;
using Finex.Application.Mappings.Catalog;
using Finex.Domain.Entities;
using Finex.Domain.ValueObjects.Catalog;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Finex.Application.Services.Catalog;

public sealed class CatalogService(
    IFinexDbContext dbContext,
    ICategoryCatalogCache catalogCache,
    ILogger<CatalogService> logger) : ICatalogService
{
    private const int MaxTreeDepth = 20;
    private const int CteRecursionGuard = 100;

    private static readonly ActivitySource ActivitySource = new("Finex.Application.Catalog");

    public async Task<Result<CategoryDetailsDto>> CreateCategoryAsync(
        CreateCategoryDto dto,
        CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity(nameof(CreateCategoryAsync));

        if (string.IsNullOrWhiteSpace(dto.Code))
            return Error.Validation("Catalog.InvalidCode", "Category code is required.");

        if (string.IsNullOrWhiteSpace(dto.Name))
            return Error.Validation("Catalog.InvalidName", "Category name is required.");

        var normalizedCode = dto.Code.Trim().ToUpperInvariant();
        var normalizedName = dto.Name.Trim();
        var normalizedDescription = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();

        CategoryAttributeSchema? schema = null;
        if (dto.ParentCategoryId is null)
        {
            if (dto.Attributes is null || dto.Attributes.Count == 0)
            {
                return Error.Validation(
                    "Catalog.RootSchemaRequired",
                    "A root category must define at least one custom attribute in its schema.");
            }

            var schemaResult = BuildValidatedSchema(dto.Attributes);
            if (schemaResult.IsFailure)
                return schemaResult.Error;

            schema = schemaResult.Value;
        }
        else if (dto.Attributes is { Count: > 0 })
        {
            return Error.Validation(
                "Catalog.SubCategorySchemaForbidden",
                "Subcategories inherit their attribute schema from the root category and cannot define their own schema.");
        }

        var codeExists = await dbContext.ProductCategories
            .AsNoTracking()
            .AnyAsync(c => c.Code == normalizedCode, ct);

        if (codeExists)
        {
            logger.LogWarning("Category creation rejected due to duplicate code {CategoryCode}", normalizedCode);
            return Error.Conflict(
                "Catalog.DuplicateConflict",
                $"A category with code '{normalizedCode}' already exists.");
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

            if (parent is null)
            {
                return Error.NotFound(
                    "Catalog.ParentNotFound",
                    $"Parent category with ID '{dto.ParentCategoryId.Value}' was not found.");
            }

            if (!parent.IsActive)
            {
                logger.LogWarning(
                    "Category creation rejected for code {CategoryCode}: target parent {ParentId} is inactive",
                    normalizedCode,
                    parent.Id);

                return Error.Failure(
                    "Catalog.ParentInactive",
                    "Cannot create a subcategory under an inactive parent category.");
            }

            if (parent.Level + 1 > MaxTreeDepth)
            {
                logger.LogWarning(
                    "Category creation rejected for code {CategoryCode}: hierarchy depth limit exceeded under parent {ParentId}",
                    normalizedCode,
                    parent.Id);

                return Error.Validation(
                    "Catalog.HierarchyRuleViolation",
                    $"The requested category hierarchy exceeds the maximum depth limit of {MaxTreeDepth} levels.");
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
                "Category creation rejected due to duplicate name '{CategoryName}' under parent {ParentId}",
                normalizedName,
                dto.ParentCategoryId);

            return Error.Conflict(
                "Catalog.DuplicateConflict",
                "A category with this name already exists at the same hierarchy level.");
        }

        var now = DateTimeOffset.UtcNow;
        var entity = new ProductCategory
        {
            Code = normalizedCode,
            Name = normalizedName,
            Description = normalizedDescription,
            ParentCategoryId = dto.ParentCategoryId,
            Level = level,
            IsActive = true,
            AttributeSchema = schema,
            CreatedAtUtc = now
        };

        dbContext.ProductCategories.Add(entity);

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            logger.LogWarning(
                ex,
                "Database unique constraint conflict while creating category {CategoryCode}",
                normalizedCode);

            return Error.Conflict(
                "Catalog.DuplicateConflict",
                "A category with this code or name already exists.");
        }

        await catalogCache.RefreshProactivelyAsync(ct);

        logger.LogInformation(
            "Created product category {CategoryId} ({CategoryCode}) at Level {Level}",
            entity.Id,
            entity.Code,
            entity.Level);

        var snapshot = await catalogCache.GetSnapshotAsync(ct);
        snapshot.EffectiveSchemaById.TryGetValue(entity.Id, out var effectiveSchema);

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
            entity.UpdatedAtUtc)
        {
            OwnAttributes = MapSchemaToDtos(entity.AttributeSchema),
            EffectiveSchema = effectiveSchema
        };
    }

    public async Task<Result<IReadOnlyList<CategoryTreeNodeDto>>> GetCategoryTreeAsync(
        bool includeInactive = false,
        CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity(nameof(GetCategoryTreeAsync));
        activity?.SetTag("catalog.include_inactive", includeInactive);

        var snapshot = await catalogCache.GetSnapshotAsync(ct);
        var tree = includeInactive ? snapshot.AllTree : snapshot.ActiveTree;

        return Result.Success(tree);
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
                $"Product category with ID '{id}' was not found.");
        }

        var snapshot = await catalogCache.GetSnapshotAsync(ct);
        IReadOnlyList<AttributeDefinitionDto>? ownAttributes = null;
        if (snapshot.ById.TryGetValue(id, out var cachedNode))
        {
            ownAttributes = MapSchemaToDtos(cachedNode.AttributeSchema);
        }

        snapshot.EffectiveSchemaById.TryGetValue(id, out var effectiveSchema);

        return dto with
        {
            OwnAttributes = ownAttributes,
            EffectiveSchema = effectiveSchema
        };
    }

    public async Task<Result<EffectiveCategorySchemaDto>> GetEffectiveSchemaAsync(
        int categoryId,
        CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity(nameof(GetEffectiveSchemaAsync));
        activity?.SetTag("catalog.category_id", categoryId);

        var snapshot = await catalogCache.GetSnapshotAsync(ct);

        if (!snapshot.ById.ContainsKey(categoryId))
        {
            return Error.NotFound(
                "Catalog.CategoryNotFound",
                $"Product category with ID '{categoryId}' was not found.");
        }

        if (!snapshot.EffectiveSchemaById.TryGetValue(categoryId, out var effectiveSchema) || effectiveSchema is null)
        {
            return Error.Validation(
                "Catalog.MissingEffectiveSchema",
                $"No attribute schema could be resolved for category '{categoryId}'.");
        }

        return effectiveSchema;
    }

    public async Task<Result<CategoryDetailsDto>> UpdateCategoryAsync(
        int id,
        UpdateCategoryDto dto,
        CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity(nameof(UpdateCategoryAsync));

        if (string.IsNullOrWhiteSpace(dto.Name))
            return Error.Validation("Catalog.InvalidName", "Category name is required.");

        var category = await dbContext.ProductCategories
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (category is null)
        {
            return Error.NotFound(
                "Catalog.CategoryNotFound",
                $"Product category with ID '{id}' was not found.");
        }

        if (!category.IsActive)
        {
            logger.LogWarning("Category update rejected for inactive category {CategoryId}", id);
            return Error.Failure(
                "Catalog.InactiveCategoryLocked",
                $"Category '{id}' is currently inactive and cannot be modified until it is activated.");
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
                "A category with this name already exists at the same hierarchy level.");
        }

        if (category.ParentCategoryId is null)
        {
            if (dto.Attributes is null || dto.Attributes.Count == 0)
            {
                return Error.Validation(
                    "Catalog.RootSchemaRequired",
                    "A root category must define at least one custom attribute in its schema.");
            }

            var newSchemaResult = BuildValidatedSchema(dto.Attributes);
            if (newSchemaResult.IsFailure)
                return newSchemaResult.Error;

            var newSchema = newSchemaResult.Value;

            if (category.AttributeSchema is not null)
            {
                var subtree = await dbContext.GetCategorySubtreeAsync(id, CteRecursionGuard, ct);
                var subtreeIds = subtree.Select(c => c.Id).ToList();

                var compatibilityResult = await ValidateSchemaEvolutionAsync(
                    category.Id,
                    category.Code,
                    category.AttributeSchema,
                    newSchema,
                    subtreeIds,
                    dto.ForceBreakingChanges,
                    ct);

                if (compatibilityResult.IsFailure)
                    return compatibilityResult.Error;
            }

            category.AttributeSchema = newSchema;
        }
        else if (dto.Attributes is { Count: > 0 })
        {
            return Error.Validation(
                "Catalog.SubCategorySchemaForbidden",
                "Subcategories inherit their attribute schema from the root category and cannot define their own schema.");
        }

        category.Name = normalizedName;
        category.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        category.UpdatedAtUtc = DateTimeOffset.UtcNow;

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            logger.LogWarning(ex, "Database unique constraint conflict while updating category {CategoryId}", id);
            return Error.Conflict(
                "Catalog.DuplicateConflict",
                "A category with this name already exists at the same hierarchy level.");
        }

        await catalogCache.RefreshProactivelyAsync(ct);

        logger.LogInformation("Updated product category {CategoryId} ({CategoryCode})", category.Id, category.Code);

        return await GetCategoryByIdAsync(id, ct);
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
                "A category cannot be assigned as its own parent.");
        }

        var newLevel = 1;
        string? newParentCode = null;
        string? newParentName = null;
        int? newRootId = null;

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
                    "The target parent category does not exist or is currently inactive.");
            }

            if (ancestors.Any(a => a.Id == id))
            {
                logger.LogWarning(
                    "Category move rejected for {CategoryId}: circular hierarchy detected via parent {NewParentId}",
                    id,
                    newParentCategoryId.Value);

                return Error.Conflict(
                    "Catalog.InvalidHierarchy",
                    "Circular hierarchy detected: a category cannot be moved under one of its descendants.");
            }

            newLevel = newParent.Level + 1;
            newParentCode = newParent.Code;
            newParentName = newParent.Name;
            newRootId = ancestors.FirstOrDefault(a => a.ParentCategoryId is null)?.Id;
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
                    $"Product category with ID '{id}' was not found.");
                return;
            }

            if (!targetEntity.IsActive)
            {
                domainError = Error.Failure(
                    "Catalog.InactiveCategoryLocked",
                    $"Category '{id}' is currently inactive and cannot be moved until it is activated.");
                return;
            }

            if (targetEntity.ParentCategoryId is null && newParentCategoryId.HasValue)
            {
                domainError = Error.Conflict(
                    "Catalog.InvalidHierarchy",
                    "A root category owns an attribute schema and cannot be moved under another category.");
                return;
            }

            if (targetEntity.ParentCategoryId.HasValue && newParentCategoryId is null)
            {
                domainError = Error.Conflict(
                    "Catalog.InvalidHierarchy",
                    "A subcategory cannot be promoted to a root category without defining a root attribute schema.");
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

            if (targetEntity.ParentCategoryId.HasValue && newRootId.HasValue)
            {
                var currentAncestors = await dbContext.GetCategoryAncestorsAsync(id, CteRecursionGuard, token);
                var currentRootId = currentAncestors.FirstOrDefault(a => a.ParentCategoryId is null)?.Id;

                if (currentRootId != newRootId)
                {
                    var subtreeIds = subtreeEntities.Select(c => c.Id).ToList();
                    var hasProducts = await dbContext.Products
                        .AsNoTracking()
                        .AnyAsync(p => subtreeIds.Contains(p.CategoryId), token);

                    if (hasProducts)
                    {
                        domainError = Error.Conflict(
                            "Catalog.SchemaInheritanceConflict",
                            "Cannot move this subcategory to a different root category because products are already assigned under it.");
                        return;
                    }
                }
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
                    "A category with this name already exists at the target hierarchy level.");
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
                        $"Moving this category would exceed the maximum hierarchy depth limit of {MaxTreeDepth} levels.");
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
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                logger.LogWarning(ex, "Database constraint conflict while moving category {CategoryId}", id);
                domainError = Error.Conflict(
                    "Catalog.DuplicateConflict",
                    "A category with this name already exists at the target hierarchy level.");
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
            return domainError;

        await catalogCache.RefreshProactivelyAsync(ct);

        var snapshot = await catalogCache.GetSnapshotAsync(ct);
        snapshot.EffectiveSchemaById.TryGetValue(id, out var effectiveSchema);

        logger.LogInformation(
            "Moved category {CategoryId} ({CategoryCode}) to parent {NewParentId} at Level {NewLevel}",
            id,
            movedDetails!.Code,
            newParentCategoryId,
            newLevel);

        return movedDetails with
        {
            OwnAttributes = snapshot.ById.TryGetValue(id, out var node)
                ? MapSchemaToDtos(node.AttributeSchema)
                : null,
            EffectiveSchema = effectiveSchema
        };
    }

    public async Task<Result> DeactivateCategoryAsync(int id, CancellationToken ct = default)
    {
        var category = await dbContext.ProductCategories
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (category is null)
        {
            return Error.NotFound(
                "Catalog.CategoryNotFound",
                $"Product category with ID '{id}' was not found.");
        }

        if (!category.IsActive)
            return Result.Success();

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
                "Catalog.HasActiveSubCategories",
                "Cannot deactivate this category while it still has active subcategories.");
        }

        var hasAssignedProducts = await dbContext.Products
            .AsNoTracking()
            .AnyAsync(p => p.CategoryId == id, ct);

        if (hasAssignedProducts)
        {
            logger.LogWarning(
                "Deactivation blocked for category {CategoryId} ({CategoryCode}): assigned products exist",
                category.Id,
                category.Code);

            return Error.Conflict(
                "Catalog.HasAssignedProducts",
                "Cannot deactivate this category while products are still assigned to it.");
        }

        category.IsActive = false;
        category.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(ct);
        await catalogCache.RefreshProactivelyAsync(ct);

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
                $"Product category with ID '{id}' was not found.");
        }

        if (category.IsActive)
            return Result.Success();

        if (category.ParentCategory is not null && !category.ParentCategory.IsActive)
        {
            logger.LogWarning(
                "Activation blocked for category {CategoryId} ({CategoryCode}): parent {ParentId} is inactive",
                category.Id,
                category.Code,
                category.ParentCategory.Id);

            return Error.Conflict(
                "Catalog.ParentInactive",
                "Cannot activate a subcategory while its parent category is inactive.");
        }

        category.IsActive = true;
        category.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(ct);
        await catalogCache.RefreshProactivelyAsync(ct);

        logger.LogInformation("Activated product category {CategoryId} ({CategoryCode})", category.Id, category.Code);

        return Result.Success();
    }


    private static Result<CategoryAttributeSchema> BuildValidatedSchema(
        IReadOnlyList<AttributeDefinitionDto> attributeDtos)
    {
        try
        {
            var definitions = attributeDtos
                .Select(a => new CategoryAttributeDefinition(
                    a.Key,
                    a.Label,
                    a.DataType,
                    a.IsRequired,
                    a.Unit,
                    a.AllowedValues))
                .ToList();

            return new CategoryAttributeSchema(definitions);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("Catalog.InvalidSchema", ex.Message);
        }
    }

    private async Task<Result> ValidateSchemaEvolutionAsync(
         int categoryId,
         string categoryCode,
         CategoryAttributeSchema existingSchema,
         CategoryAttributeSchema newSchema,
         IReadOnlyCollection<int> subtreeIds,
         bool forceBreakingChanges,
         CancellationToken ct)
    {
        var oldByKey = existingSchema.Attributes.ToDictionary(
            a => a.Key,
            StringComparer.OrdinalIgnoreCase);

        var newByKey = newSchema.Attributes.ToDictionary(
            a => a.Key,
            StringComparer.OrdinalIgnoreCase);

        var breakingReasons = new List<string>();

        foreach (var oldAttr in existingSchema.Attributes)
        {
            if (!newByKey.TryGetValue(oldAttr.Key, out var newAttr))
            {
                var affectedCount = await dbContext.CountProductsWithAttributeKeyAsync(
                    subtreeIds,
                    oldAttr.Key,
                    ct);

                if (affectedCount > 0)
                {
                    breakingReasons.Add(
                        $"Attribute '{oldAttr.Key}' is currently populated on {affectedCount} product(s).");
                }

                continue;
            }

            if (newAttr.DataType != oldAttr.DataType)
            {
                var affectedCount = await dbContext.CountProductsWithAttributeKeyAsync(
                    subtreeIds,
                    oldAttr.Key,
                    ct);

                if (affectedCount > 0)
                {
                    breakingReasons.Add(
                        $"Changing data type of '{oldAttr.Key}' from '{oldAttr.DataType}' to '{newAttr.DataType}' affects {affectedCount} existing product(s).");
                }
            }

            if (!oldAttr.IsRequired && newAttr.IsRequired)
            {
                var totalProducts = await dbContext.Products
                    .AsNoTracking()
                    .CountAsync(p => subtreeIds.Contains(p.CategoryId), ct);

                var populatedCount = await dbContext.CountProductsWithAttributeKeyAsync(
                    subtreeIds,
                    oldAttr.Key,
                    ct);

                var missingCount = totalProducts - populatedCount;
                if (missingCount > 0)
                {
                    breakingReasons.Add(
                        $"Making '{oldAttr.Key}' required leaves {missingCount} existing product(s) without a value.");
                }
            }

            if (oldAttr.AllowedValues is { Count: > 0 } && newAttr.AllowedValues is { Count: > 0 })
            {
                var newAllowedSet = new HashSet<string>(newAttr.AllowedValues, StringComparer.OrdinalIgnoreCase);
                foreach (var removedValue in oldAttr.AllowedValues.Where(v => !newAllowedSet.Contains(v)))
                {
                    var affectedCount = await dbContext.CountProductsWithAttributeValueAsync(
                        subtreeIds,
                        oldAttr.Key,
                        removedValue,
                        ct);

                    if (affectedCount > 0)
                    {
                        breakingReasons.Add(
                            $"Allowed value '{removedValue}' on '{oldAttr.Key}' is used by {affectedCount} existing product(s).");
                    }
                }
            }
        }

        foreach (var newAttr in newSchema.Attributes.Where(a => !oldByKey.ContainsKey(a.Key)))
        {
            if (newAttr.IsRequired)
            {
                var existingProductsCount = await dbContext.Products
                    .AsNoTracking()
                    .CountAsync(p => subtreeIds.Contains(p.CategoryId), ct);

                if (existingProductsCount > 0)
                {
                    breakingReasons.Add(
                        $"New required attribute '{newAttr.Key}' will not be present on {existingProductsCount} existing product(s).");
                }
            }
        }

        if (breakingReasons.Count == 0)
            return Result.Success();

        if (!forceBreakingChanges)
        {
            return Error.Conflict(
                "Catalog.SchemaBreakingChange",
                $"{string.Join(" ", breakingReasons)} Set 'forceBreakingChanges = true' to confirm and apply this schema update intentionally.");
        }

        logger.LogWarning(
            "Manager forced breaking schema changes on category {CategoryId} ({CategoryCode}): {Reasons}",
            categoryId,
            categoryCode,
            string.Join(" | ", breakingReasons));

        return Result.Success();
    }

    private static IReadOnlyList<AttributeDefinitionDto>? MapSchemaToDtos(CategoryAttributeSchema? schema)
    {
        if (schema is null)
            return null;

        return schema.Attributes
            .Select(a => new AttributeDefinitionDto(
                a.Key,
                a.Label,
                a.DataType,
                a.IsRequired,
                a.Unit,
                a.AllowedValues))
            .ToList()
            .AsReadOnly();
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
        => ex.InnerException is SqlException { Number: 2601 or 2627 };
}