using Finex.Application.Common.Results;
using Finex.Application.DTOs.Catalog;

namespace Finex.Application.Interfaces.Catalog;

public interface ICatalogService
{
    Task<Result<IReadOnlyList<CategoryFlatNodeDto>>> GetCategoriesAsync(
        bool includeInactive = false,
        int? parentCategoryId = null,
        int? level = null,
        string? search = null,
        CancellationToken ct = default);

    Task<Result<CategoryDetailsDto>> GetCategoryByCodeAsync(
        string code,
        CancellationToken ct = default);

    Task<Result> DeleteCategoryAsync(
        int id,
        CancellationToken ct = default);

    Task<Result<CategoryDetailsDto>> CreateCategoryAsync(
        CreateCategoryDto dto,
        CancellationToken ct = default);

    Task<Result<IReadOnlyList<CategoryTreeNodeDto>>> GetCategoryTreeAsync(
        bool includeInactive = false,
        CancellationToken ct = default);

    Task<Result<CategoryDetailsDto>> GetCategoryByIdAsync(
        int id,
        CancellationToken ct = default);

    Task<Result<EffectiveCategorySchemaDto>> GetEffectiveSchemaAsync(
        int categoryId,
        CancellationToken ct = default);

    Task<Result<CategoryDetailsDto>> UpdateCategoryAsync(
        int id,
        UpdateCategoryDto dto,
        CancellationToken ct = default);

    Task<Result<CategoryDetailsDto>> MoveCategoryAsync(
        int id,
        MoveCategoryDto dto,
        CancellationToken ct = default);

    Task<Result> DeactivateCategoryAsync(
        int id,
        CancellationToken ct = default);

    Task<Result> ActivateCategoryAsync(
        int id,
        CancellationToken ct = default);
}