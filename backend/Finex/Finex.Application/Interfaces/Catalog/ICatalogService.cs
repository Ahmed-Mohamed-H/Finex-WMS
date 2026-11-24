using Finex.Application.Common.Results;
using Finex.Application.DTOs.Catalog;

namespace Finex.Application.Interfaces.Catalog;

public interface ICatalogService
{
    Task<Result<CategoryDetailsDto>> CreateCategoryAsync(
        CreateCategoryDto dto,
        CancellationToken ct = default);

    Task<Result<IReadOnlyList<CategoryTreeNodeDto>>> GetCategoryTreeAsync(
        bool includeInactive = false,
        CancellationToken ct = default);

    Task<Result<CategoryDetailsDto>> GetCategoryByIdAsync(
        int id,
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