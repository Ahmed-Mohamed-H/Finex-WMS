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
using System.Diagnostics;

namespace Finex.Application.Services.Catalog;

public sealed class CatalogService(
    IFinexDbContext dbContext,
    ICategoryCatalogCache catalogCache,
    ILogger<CatalogService> logger) : ICatalogService
{
    private const int MaxTreeDepth = 20;
    private const int CteRecursionGuard = 100;

    private static readonly ActivitySource FallbackActivitySource = new("Finex.WMS.API");

    public async Task<Result<IReadOnlyList<CategoryFlatNodeDto>>> GetCategoriesAsync(
        bool includeInactive = false,
        int? parentCategoryId = null,
        int? level = null,
        string? search = null,
        CancellationToken ct = default)
    {
        using var activity = StartActivity("CatalogService.GetCategories");
        activity?.SetTag("catalog.include_inactive", includeInactive);
        activity?.SetTag("catalog.parent_id", parentCategoryId);
        activity?.SetTag("catalog.level", level);
        activity?.SetTag("catalog.has_search", !string.IsNullOrWhiteSpace(search));

        var snapshot = await catalogCache.GetSnapshotAsync(ct);
        IEnumerable<CategorySnapshotNode> query = snapshot.ById.Values;

        if (!includeInactive)
            query = query.Where(c => c.IsActive);

        if (parentCategoryId.HasValue)
            query = query.Where(c => c.ParentCategoryId == parentCategoryId.Value);

        if (level.HasValue)
            query = query.Where(c => c.Level == level.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(c =>
                c.Code.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                c.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var items = query
            .OrderBy(c => c.Level)
            .ThenBy(c => c.Name)
            .Select(c => new CategoryFlatNodeDto(
                c.Id,
                c.Code,
                c.Name,
                c.Description,
                c.Level,
                c.IsActive,
                c.ParentCategoryId))
            .ToList()
            .AsReadOnly();

        activity?.SetTag("catalog.results_count", items.Count);

        logger.LogDebug(
            "Retrieved {ResultCount} flat product categories (IncludeInactive: {IncludeInactive}, ParentId: {ParentId}, Level: {Level}).",
            items.Count,
            includeInactive,
            parentCategoryId,
            level);

        return Result.Success<IReadOnlyList<CategoryFlatNodeDto>>(items);
    }

    public async Task<Result<CategoryDetailsDto>> GetCategoryByCodeAsync(
        string code,
        CancellationToken ct = default)
    {
        using var activity = StartActivity("CatalogService.GetCategoryByCode");

        if (string.IsNullOrWhiteSpace(code))
        {
            var error = Error.Validation("Catalog.InvalidCode", "Category code is required.");
            activity?.SetStatus(ActivityStatusCode.Error, error.Code);
            return error;
        }

        var normalizedCode = code.Trim().ToUpperInvariant();
        activity?.SetTag("catalog.category_code", normalizedCode);

        var snapshot = await catalogCache.GetSnapshotAsync(ct);

        if (!snapshot.IdByCode.TryGetValue(normalizedCode, out var id))
        {
            var error = Error.NotFound(
                "Catalog.CategoryNotFound",
                $"Product category with code '{normalizedCode}' was not found.");

            activity?.SetStatus(ActivityStatusCode.Error, error.Code);
            logger.LogWarning(
                "Category lookup failed ({ErrorCode}) for code {CategoryCode}.",
                error.Code,
                normalizedCode);

            return error;
        }

        activity?.SetTag("catalog.category_id", id);
        return await GetCategoryByIdAsync(id, ct);
    }

    public async Task<Result> DeleteCategoryAsync(int id, CancellationToken ct = default)
    {
        using var activity = StartActivity("CatalogService.DeleteCategory");
        activity?.SetTag("catalog.category_id", id);

        var category = await dbContext.ProductCategories
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (category is null)
        {
            var error = Error.NotFound(
                "Catalog.CategoryNotFound",
                $"Product category with ID '{id}' was not found.");

            activity?.SetStatus(ActivityStatusCode.Error, error.Code);
            logger.LogWarning("Category deletion failed ({ErrorCode}) for ID {CategoryId}.", error.Code, id);
            return error;
        }

        activity?.SetTag("catalog.category_code", category.Code);

        var hasAnyChildren = await dbContext.ProductCategories
            .AsNoTracking()
            .AnyAsync(c => c.ParentCategoryId == id, ct);

        if (hasAnyChildren)
        {
            var error = Error.Conflict(
                "Catalog.HasSubCategories",
                "Cannot delete this category because it has subcategories. Delete or move its subcategories first.");

            activity?.SetStatus(ActivityStatusCode.Error, error.Code);
            logger.LogWarning(
                "Category deletion rejected ({ErrorCode}) for {CategoryId} ({CategoryCode}): subcategories exist.",
                error.Code,
                category.Id,
                category.Code);

            return error;
        }

        var hasAssignedProducts = await dbContext.Products
            .AsNoTracking()
            .AnyAsync(p => p.CategoryId == id, ct);

        if (hasAssignedProducts)
        {
            var error = Error.Conflict(
                "Catalog.HasAssignedProducts",
                "Cannot delete this category because products are assigned to it. Deactivate the category instead.");

            activity?.SetStatus(ActivityStatusCode.Error, error.Code);
            logger.LogWarning(
                "Category deletion rejected ({ErrorCode}) for {CategoryId} ({CategoryCode}): assigned products exist.",
                error.Code,
                category.Id,
                category.Code);

            return error;
        }

        dbContext.ProductCategories.Remove(category);
        await dbContext.SaveChangesAsync(ct);
        await catalogCache.RefreshProactivelyAsync(ct);

        logger.LogInformation(
            "Deleted unused product category {CategoryId} ({CategoryCode}).",
            category.Id,
            category.Code);

        return Result.Success();
    }

    public async Task<Result<CategoryDetailsDto>> CreateCategoryAsync(
        CreateCategoryDto dto,
        CancellationToken ct = default)
    {
        using var activity = StartActivity("CatalogService.CreateCategory");
        activity?.SetTag("catalog.parent_id", dto.ParentCategoryId);
        activity?.SetTag("catalog.is_root", dto.ParentCategoryId is null);

        if (string.IsNullOrWhiteSpace(dto.Code))
        {
            var error = Error.Validation("Catalog.InvalidCode", "Category code is required.");
            activity?.SetStatus(ActivityStatusCode.Error, error.Code);
            return error;
        }

        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            var error = Error.Validation("Catalog.InvalidName", "Category name is required.");
            activity?.SetStatus(ActivityStatusCode.Error, error.Code);
            return error;
        }

        var normalizedCode = dto.Code.Trim().ToUpperInvariant();
        var normalizedName = dto.Name.Trim();
        var normalizedDescription = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();

        activity?.SetTag("catalog.category_code", normalizedCode);

        CategoryAttributeSchema? schema = null;
        if (dto.ParentCategoryId is null)
        {
            if (dto.Attributes is null || dto.Attributes.Count == 0)
            {
                var error = Error.Validation(
                    "Catalog.RootSchemaRequired",
                    "A root category must define at least one custom attribute in its schema.");

                activity?.SetStatus(ActivityStatusCode.Error, error.Code);
                logger.LogWarning(
                    "Root category creation rejected ({ErrorCode}) for code {CategoryCode}: missing attribute schema.",
                    error.Code,
                    normalizedCode);

                return error;
            }

            var schemaResult = BuildValidatedSchema(dto.Attributes);
            if (schemaResult.IsFailure)
            {
                activity?.SetStatus(ActivityStatusCode.Error, schemaResult.Error.Code);
                logger.LogWarning(
                    "Category creation rejected ({ErrorCode}) for code {CategoryCode}: {Reason}",
                    schemaResult.Error.Code,
                    normalizedCode,
                    schemaResult.Error.Description);

                return schemaResult.Error;
            }

            schema = schemaResult.Value;
            activity?.SetTag("catalog.schema_attributes_count", schema.Attributes.Count);
        }
        else if (dto.Attributes is { Count: > 0 })
        {
            var error = Error.Validation(
                "Catalog.SubCategorySchemaForbidden",
                "Subcategories inherit their attribute schema from the root category and cannot define their own schema.");

            activity?.SetStatus(ActivityStatusCode.Error, error.Code);
            logger.LogWarning(
                "Subcategory creation rejected ({ErrorCode}) for code {CategoryCode} under parent {ParentId}: custom schema forbidden.",
                error.Code,
                normalizedCode,
                dto.ParentCategoryId);

            return error;
        }

        var codeExists = await dbContext.ProductCategories
            .AsNoTracking()
            .AnyAsync(c => c.Code == normalizedCode, ct);

        if (codeExists)
        {
            var error = Error.Conflict(
                "Catalog.DuplicateConflict",
                $"A category with code '{normalizedCode}' already exists.");

            activity?.SetStatus(ActivityStatusCode.Error, error.Code);
            logger.LogWarning(
                "Category creation rejected ({ErrorCode}) due to duplicate code {CategoryCode}.",
                error.Code,
                normalizedCode);

            return error;
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
                var error = Error.NotFound(
                    "Catalog.ParentNotFound",
                    $"Parent category with ID '{dto.ParentCategoryId.Value}' was not found.");

                activity?.SetStatus(ActivityStatusCode.Error, error.Code);
                logger.LogWarning(
                    "Category creation rejected ({ErrorCode}) for code {CategoryCode}: parent {ParentId} not found.",
                    error.Code,
                    normalizedCode,
                    dto.ParentCategoryId.Value);

                return error;
            }

            if (!parent.IsActive)
            {
                var error = Error.Failure(
                    "Catalog.ParentInactive",
                    "Cannot create a subcategory under an inactive parent category.");

                activity?.SetStatus(ActivityStatusCode.Error, error.Code);
                logger.LogWarning(
                    "Category creation rejected ({ErrorCode}) for code {CategoryCode}: target parent {ParentId} is inactive.",
                    error.Code,
                    normalizedCode,
                    parent.Id);

                return error;
            }

            if (parent.Level + 1 > MaxTreeDepth)
            {
                var error = Error.Validation(
                    "Catalog.HierarchyRuleViolation",
                    $"The requested category hierarchy exceeds the maximum depth limit of {MaxTreeDepth} levels.");

                activity?.SetStatus(ActivityStatusCode.Error, error.Code);
                logger.LogWarning(
                    "Category creation rejected ({ErrorCode}) for code {CategoryCode}: hierarchy depth limit exceeded under parent {ParentId}.",
                    error.Code,
                    normalizedCode,
                    parent.Id);

                return error;
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
            var error = Error.Conflict(
                "Catalog.DuplicateConflict",
                "A category with this name already exists at the same hierarchy level.");

            activity?.SetStatus(ActivityStatusCode.Error, error.Code);
            logger.LogWarning(
                "Category creation rejected ({ErrorCode}) due to duplicate name '{CategoryName}' under parent {ParentId}.",
                error.Code,
                normalizedName,
                dto.ParentCategoryId);

            return error;
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
            activity?.SetStatus(ActivityStatusCode.Error, "Catalog.DuplicateConflict");
            logger.LogWarning(
                ex,
                "Database unique constraint conflict while creating category {CategoryCode}.",
                normalizedCode);

            return Error.Conflict(
                "Catalog.DuplicateConflict",
                "A category with this code or name already exists.");
        }

        activity?.SetTag("catalog.category_id", entity.Id);
        activity?.SetTag("catalog.level", entity.Level);

        await catalogCache.RefreshProactivelyAsync(ct);

        logger.LogInformation(
            "Created product category {CategoryId} ({CategoryCode}) at Level {Level} under Parent {ParentId} with {AttributeCount} schema attributes.",
            entity.Id,
            entity.Code,
            entity.Level,
            entity.ParentCategoryId,
            schema?.Attributes.Count ?? 0);

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
        using var activity = StartActivity("CatalogService.GetCategoryTree");
        activity?.SetTag("catalog.include_inactive", includeInactive);

        var snapshot = await catalogCache.GetSnapshotAsync(ct);
        var tree = includeInactive ? snapshot.AllTree : snapshot.ActiveTree;

        activity?.SetTag("catalog.root_nodes_count", tree.Count);

        return Result.Success(tree);
    }

    public async Task<Result<CategoryDetailsDto>> GetCategoryByIdAsync(
        int id,
        CancellationToken ct = default)
    {
        using var activity = StartActivity("CatalogService.GetCategoryById");
        activity?.SetTag("catalog.category_id", id);

        var dto = await dbContext.ProductCategories
            .AsNoTracking()
            .Where(c => c.Id == id)
            .ProjectToDetailsDto()
            .FirstOrDefaultAsync(ct);

        if (dto is null)
        {
            var error = Error.NotFound(
                "Catalog.CategoryNotFound",
                $"Product category with ID '{id}' was not found.");

            activity?.SetStatus(ActivityStatusCode.Error, error.Code);
            logger.LogWarning("Product category {CategoryId} was not found.", id);
            return error;
        }

        activity?.SetTag("catalog.category_code", dto.Code);

        var snapshot = await catalogCache.GetSnapshotAsync(ct);
        IReadOnlyList<AttributeDefinitionDto>? ownAttributes = null;
        if (snapshot.ById.TryGetValue(id, out var cachedNode))
        {
            ownAttributes = MapSchemaToDtos(cachedNode.AttributeSchema);
        }

        snapshot.EffectiveSchemaById.TryGetValue(id, out var effectiveSchema);
        activity?.SetTag("catalog.has_effective_schema", effectiveSchema is not null);

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
        using var activity = StartActivity("CatalogService.GetEffectiveSchema");
        activity?.SetTag("catalog.category_id", categoryId);

        var snapshot = await catalogCache.GetSnapshotAsync(ct);

        if (!snapshot.ById.ContainsKey(categoryId))
        {
            var error = Error.NotFound(
                "Catalog.CategoryNotFound",
                $"Product category with ID '{categoryId}' was not found.");

            activity?.SetStatus(ActivityStatusCode.Error, error.Code);
            logger.LogWarning("Effective schema lookup failed ({ErrorCode}) for category {CategoryId}.", error.Code, categoryId);
            return error;
        }

        if (!snapshot.EffectiveSchemaById.TryGetValue(categoryId, out var effectiveSchema) || effectiveSchema is null)
        {
            var error = Error.Validation(
                "Catalog.MissingEffectiveSchema",
                $"No attribute schema could be resolved for category '{categoryId}'.");

            activity?.SetStatus(ActivityStatusCode.Error, error.Code);
            logger.LogWarning("Category {CategoryId} has no resolvable root attribute schema ({ErrorCode}).", categoryId, error.Code);
            return error;
        }

        activity?.SetTag("catalog.source_root_id", effectiveSchema.SourceRootCategoryId);
        activity?.SetTag("catalog.attributes_count", effectiveSchema.Attributes.Count);

        return effectiveSchema;
    }

    public async Task<Result<CategoryDetailsDto>> UpdateCategoryAsync(
        int id,
        UpdateCategoryDto dto,
        CancellationToken ct = default)
    {
        using var activity = StartActivity("CatalogService.UpdateCategory");
        activity?.SetTag("catalog.category_id", id);
        activity?.SetTag("catalog.force_breaking_changes", dto.ForceBreakingChanges);

        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            var error = Error.Validation("Catalog.InvalidName", "Category name is required.");
            activity?.SetStatus(ActivityStatusCode.Error, error.Code);
            return error;
        }

        var category = await dbContext.ProductCategories
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (category is null)
        {
            var error = Error.NotFound(
                "Catalog.CategoryNotFound",
                $"Product category with ID '{id}' was not found.");

            activity?.SetStatus(ActivityStatusCode.Error, error.Code);
            logger.LogWarning("Category update failed ({ErrorCode}) for ID {CategoryId}.", error.Code, id);
            return error;
        }

        activity?.SetTag("catalog.category_code", category.Code);

        if (!category.IsActive)
        {
            var error = Error.Failure(
                "Catalog.InactiveCategoryLocked",
                $"Category '{id}' is currently inactive and cannot be modified until it is activated.");

            activity?.SetStatus(ActivityStatusCode.Error, error.Code);
            logger.LogWarning(
                "Category update rejected ({ErrorCode}) for inactive category {CategoryId} ({CategoryCode}).",
                error.Code,
                id,
                category.Code);

            return error;
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
            var error = Error.Conflict(
                "Catalog.DuplicateConflict",
                "A category with this name already exists at the same hierarchy level.");

            activity?.SetStatus(ActivityStatusCode.Error, error.Code);
            logger.LogWarning(
                "Category update rejected ({ErrorCode}) for {CategoryId} ({CategoryCode}) due to duplicate name under parent {ParentId}.",
                error.Code,
                id,
                category.Code,
                category.ParentCategoryId);

            return error;
        }

        if (category.ParentCategoryId is null)
        {
            if (dto.Attributes is null || dto.Attributes.Count == 0)
            {
                var error = Error.Validation(
                    "Catalog.RootSchemaRequired",
                    "A root category must define at least one custom attribute in its schema.");

                activity?.SetStatus(ActivityStatusCode.Error, error.Code);
                return error;
            }

            var newSchemaResult = BuildValidatedSchema(dto.Attributes);
            if (newSchemaResult.IsFailure)
            {
                activity?.SetStatus(ActivityStatusCode.Error, newSchemaResult.Error.Code);
                logger.LogWarning(
                    "Category schema update rejected ({ErrorCode}) for {CategoryId} ({CategoryCode}): {Reason}",
                    newSchemaResult.Error.Code,
                    id,
                    category.Code,
                    newSchemaResult.Error.Description);

                return newSchemaResult.Error;
            }

            var newSchema = newSchemaResult.Value;
            activity?.SetTag("catalog.schema_attributes_count", newSchema.Attributes.Count);

            if (category.AttributeSchema is not null)
            {
                var subtree = await dbContext.GetCategorySubtreeAsync(id, CteRecursionGuard, ct);
                var subtreeIds = subtree.Select(c => c.Id).ToList();

                var compatibilityResult = await ValidateSchemaEvolutionAsync(
                    activity,
                    category.Id,
                    category.Code,
                    category.AttributeSchema,
                    newSchema,
                    subtreeIds,
                    dto.ForceBreakingChanges,
                    ct);

                if (compatibilityResult.IsFailure)
                {
                    activity?.SetStatus(ActivityStatusCode.Error, compatibilityResult.Error.Code);
                    return compatibilityResult.Error;
                }
            }

            category.AttributeSchema = newSchema;
        }
        else if (dto.Attributes is { Count: > 0 })
        {
            var error = Error.Validation(
                "Catalog.SubCategorySchemaForbidden",
                "Subcategories inherit their attribute schema from the root category and cannot define their own schema.");

            activity?.SetStatus(ActivityStatusCode.Error, error.Code);
            return error;
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
            activity?.SetStatus(ActivityStatusCode.Error, "Catalog.DuplicateConflict");
            logger.LogWarning(
                ex,
                "Database unique constraint conflict while updating category {CategoryId} ({CategoryCode}).",
                id,
                category.Code);

            return Error.Conflict(
                "Catalog.DuplicateConflict",
                "A category with this name already exists at the same hierarchy level.");
        }

        await catalogCache.RefreshProactivelyAsync(ct);

        logger.LogInformation(
            "Updated product category {CategoryId} ({CategoryCode}) (ForceBreakingChanges: {ForceBreakingChanges}).",
            category.Id,
            category.Code,
            dto.ForceBreakingChanges);

        return await GetCategoryByIdAsync(id, ct);
    }

    public async Task<Result<CategoryDetailsDto>> MoveCategoryAsync(
        int id,
        MoveCategoryDto dto,
        CancellationToken ct = default)
    {
        using var activity = StartActivity("CatalogService.MoveCategory");
        activity?.SetTag("catalog.category_id", id);
        activity?.SetTag("catalog.new_parent_id", dto.NewParentCategoryId);

        var newParentCategoryId = dto.NewParentCategoryId;

        if (newParentCategoryId == id)
        {
            var error = Error.Conflict(
                "Catalog.InvalidHierarchy",
                "A category cannot be assigned as its own parent.");

            activity?.SetStatus(ActivityStatusCode.Error, error.Code);
            logger.LogWarning(
                "Category move rejected ({ErrorCode}) for {CategoryId}: self-parent assignment attempted.",
                error.Code,
                id);

            return error;
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
                var error = Error.Conflict(
                    "Catalog.InvalidHierarchy",
                    "The target parent category does not exist or is currently inactive.");

                activity?.SetStatus(ActivityStatusCode.Error, error.Code);
                logger.LogWarning(
                    "Category move rejected ({ErrorCode}) for {CategoryId}: target parent {NewParentId} is missing or inactive.",
                    error.Code,
                    id,
                    newParentCategoryId.Value);

                return error;
            }

            if (ancestors.Any(a => a.Id == id))
            {
                var error = Error.Conflict(
                    "Catalog.InvalidHierarchy",
                    "Circular hierarchy detected: a category cannot be moved under one of its descendants.");

                activity?.SetStatus(ActivityStatusCode.Error, error.Code);
                logger.LogWarning(
                    "Category move rejected ({ErrorCode}) for {CategoryId}: circular hierarchy detected via parent {NewParentId}.",
                    error.Code,
                    id,
                    newParentCategoryId.Value);

                return error;
            }

            newLevel = newParent.Level + 1;
            newParentCode = newParent.Code;
            newParentName = newParent.Name;
            newRootId = ancestors.FirstOrDefault(a => a.ParentCategoryId is null)?.Id;
        }

        CategoryDetailsDto? movedDetails = null;
        Error? domainError = null;
        var descendantsUpdated = 0;

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

            activity?.SetTag("catalog.category_code", targetEntity.Code);
            activity?.SetTag("catalog.old_level", targetEntity.Level);

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
                    descendantsUpdated++;
                }
            }

            try
            {
                await dbContext.SaveChangesAsync(token);
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                logger.LogWarning(ex, "Database constraint conflict while moving category {CategoryId}.", id);
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
        {
            activity?.SetStatus(ActivityStatusCode.Error, domainError.Code);
            logger.LogWarning(
                "Category move rejected ({ErrorCode}) for {CategoryId} to parent {NewParentId}: {Reason}",
                domainError.Code,
                id,
                newParentCategoryId,
                domainError.Description);

            return domainError;
        }

        activity?.SetTag("catalog.new_level", newLevel);
        activity?.SetTag("catalog.affected_descendants_count", descendantsUpdated);

        await catalogCache.RefreshProactivelyAsync(ct);

        var snapshot = await catalogCache.GetSnapshotAsync(ct);
        snapshot.EffectiveSchemaById.TryGetValue(id, out var effectiveSchema);

        logger.LogInformation(
            "Moved category {CategoryId} ({CategoryCode}) to parent {NewParentId} at Level {NewLevel} ({DescendantsUpdated} descendants shifted).",
            id,
            movedDetails!.Code,
            newParentCategoryId,
            newLevel,
            descendantsUpdated);

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
        using var activity = StartActivity("CatalogService.DeactivateCategory");
        activity?.SetTag("catalog.category_id", id);

        var category = await dbContext.ProductCategories
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (category is null)
        {
            var error = Error.NotFound(
                "Catalog.CategoryNotFound",
                $"Product category with ID '{id}' was not found.");

            activity?.SetStatus(ActivityStatusCode.Error, error.Code);
            logger.LogWarning("Category deactivation failed ({ErrorCode}) for ID {CategoryId}.", error.Code, id);
            return error;
        }

        activity?.SetTag("catalog.category_code", category.Code);

        if (!category.IsActive)
            return Result.Success();

        var hasActiveChildren = await dbContext.ProductCategories
            .AsNoTracking()
            .AnyAsync(c => c.ParentCategoryId == id && c.IsActive, ct);

        if (hasActiveChildren)
        {
            var error = Error.Conflict(
                "Catalog.HasActiveSubCategories",
                "Cannot deactivate this category while it still has active subcategories.");

            activity?.SetStatus(ActivityStatusCode.Error, error.Code);
            logger.LogWarning(
                "Deactivation blocked ({ErrorCode}) for category {CategoryId} ({CategoryCode}): active subcategories exist.",
                error.Code,
                category.Id,
                category.Code);

            return error;
        }

        var hasAssignedProducts = await dbContext.Products
            .AsNoTracking()
            .AnyAsync(p => p.CategoryId == id, ct);

        if (hasAssignedProducts)
        {
            var error = Error.Conflict(
                "Catalog.HasAssignedProducts",
                "Cannot deactivate this category while products are still assigned to it.");

            activity?.SetStatus(ActivityStatusCode.Error, error.Code);
            logger.LogWarning(
                "Deactivation blocked ({ErrorCode}) for category {CategoryId} ({CategoryCode}): assigned products exist.",
                error.Code,
                category.Id,
                category.Code);

            return error;
        }

        category.IsActive = false;
        category.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(ct);
        await catalogCache.RefreshProactivelyAsync(ct);

        logger.LogInformation("Deactivated product category {CategoryId} ({CategoryCode}).", category.Id, category.Code);

        return Result.Success();
    }

    public async Task<Result> ActivateCategoryAsync(int id, CancellationToken ct = default)
    {
        using var activity = StartActivity("CatalogService.ActivateCategory");
        activity?.SetTag("catalog.category_id", id);

        var category = await dbContext.ProductCategories
            .Include(c => c.ParentCategory)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (category is null)
        {
            var error = Error.NotFound(
                "Catalog.CategoryNotFound",
                $"Product category with ID '{id}' was not found.");

            activity?.SetStatus(ActivityStatusCode.Error, error.Code);
            logger.LogWarning("Category activation failed ({ErrorCode}) for ID {CategoryId}.", error.Code, id);
            return error;
        }

        activity?.SetTag("catalog.category_code", category.Code);
        activity?.SetTag("catalog.parent_id", category.ParentCategoryId);

        if (category.IsActive)
            return Result.Success();

        if (category.ParentCategory is not null && !category.ParentCategory.IsActive)
        {
            var error = Error.Conflict(
                "Catalog.ParentInactive",
                "Cannot activate a subcategory while its parent category is inactive.");

            activity?.SetStatus(ActivityStatusCode.Error, error.Code);
            logger.LogWarning(
                "Activation blocked ({ErrorCode}) for category {CategoryId} ({CategoryCode}): parent {ParentId} is inactive.",
                error.Code,
                category.Id,
                category.Code,
                category.ParentCategory.Id);

            return error;
        }

        category.IsActive = true;
        category.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(ct);
        await catalogCache.RefreshProactivelyAsync(ct);

        logger.LogInformation("Activated product category {CategoryId} ({CategoryCode}).", category.Id, category.Code);

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
        Activity? activity,
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

        activity?.AddEvent(new ActivityEvent(
            "Breaking schema changes detected",
            tags: new ActivityTagsCollection
            {
                { "catalog.breaking_reasons_count", breakingReasons.Count },
                { "catalog.force_breaking_changes", forceBreakingChanges }
            }));

        if (!forceBreakingChanges)
        {
            logger.LogWarning(
                "Breaking schema update blocked ({ErrorCode}) for category {CategoryId} ({CategoryCode}): {Reasons}",
                "Catalog.SchemaBreakingChange",
                categoryId,
                categoryCode,
                string.Join(" | ", breakingReasons));

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

    private static Activity? StartActivity(string operationName)
        => Activity.Current?.Source.StartActivity(operationName, ActivityKind.Internal)
           ?? FallbackActivitySource.StartActivity(operationName, ActivityKind.Internal);

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
        => ex.InnerException is SqlException { Number: 2601 or 2627 };
}