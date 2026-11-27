using Finex.API.Contracts.Common;
using Finex.API.Observability;
using Finex.Application.DTOs.Catalog;
using Finex.Application.Interfaces.Catalog;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace Finex.API.Controllers
{
    [Route("api/catalog/categories")]
    public sealed class CategoriesController(ICatalogService catalogService) : ApiControllerBase
    {
        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CategoryFlatNodeDto>>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<IReadOnlyList<CategoryFlatNodeDto>>>> GetAll(
            [FromQuery] bool includeInactive = false,
            [FromQuery] int? parentCategoryId = null,
            [FromQuery] int? level = null,
            [FromQuery] string? search = null,
            CancellationToken ct = default)
        {
            using var activity = FinexDiagnostics.ActivitySource.StartActivity(
                "CategoriesController.GetAll",
                ActivityKind.Internal);

            activity?.SetTag("catalog.include_inactive", includeInactive);
            activity?.SetTag("catalog.parent_id", parentCategoryId);
            activity?.SetTag("catalog.level", level);

            var result = await catalogService.GetCategoriesAsync(
                includeInactive,
                parentCategoryId,
                level,
                search,
                ct);

            if (result.IsFailure)
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.Error.Code);
                activity?.SetTag("error.code", result.Error.Code);
                return FromResult(result);
            }

            return FromResult(result, "Categories retrieved successfully.");
        }

        [HttpGet("tree")]
        [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CategoryTreeNodeDto>>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<IReadOnlyList<CategoryTreeNodeDto>>>> GetTree(
            [FromQuery] bool includeInactive = false,
            CancellationToken ct = default)
        {
            using var activity = FinexDiagnostics.ActivitySource.StartActivity(
                "CategoriesController.GetTree",
                ActivityKind.Internal);

            activity?.SetTag("catalog.include_inactive", includeInactive);

            var result = await catalogService.GetCategoryTreeAsync(includeInactive, ct);

            if (result.IsFailure)
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.Error.Code);
                activity?.SetTag("error.code", result.Error.Code);
                return FromResult(result);
            }

            return FromResult(result, "Category tree retrieved successfully.");
        }

        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(ApiResponse<CategoryDetailsDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ApiResponse<CategoryDetailsDto>>> GetById(
            int id,
            CancellationToken ct = default)
        {
            using var activity = FinexDiagnostics.ActivitySource.StartActivity(
                "CategoriesController.GetById",
                ActivityKind.Internal);

            activity?.SetTag("catalog.category_id", id);

            var result = await catalogService.GetCategoryByIdAsync(id, ct);

            if (result.IsFailure)
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.Error.Code);
                activity?.SetTag("error.code", result.Error.Code);
                return FromResult(result);
            }

            return FromResult(result, "Category details retrieved successfully.");
        }

        [HttpGet("by-code/{code}")]
        [ProducesResponseType(typeof(ApiResponse<CategoryDetailsDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ApiResponse<CategoryDetailsDto>>> GetByCode(
            string code,
            CancellationToken ct = default)
        {
            using var activity = FinexDiagnostics.ActivitySource.StartActivity(
                "CategoriesController.GetByCode",
                ActivityKind.Internal);

            activity?.SetTag("catalog.category_code", code);

            var result = await catalogService.GetCategoryByCodeAsync(code, ct);

            if (result.IsFailure)
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.Error.Code);
                activity?.SetTag("error.code", result.Error.Code);
                return FromResult(result);
            }

            return FromResult(result, "Category details retrieved successfully.");
        }

        [HttpGet("{id:int}/effective-schema")]
        [ProducesResponseType(typeof(ApiResponse<EffectiveCategorySchemaDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ApiResponse<EffectiveCategorySchemaDto>>> GetEffectiveSchema(
            int id,
            CancellationToken ct = default)
        {
            using var activity = FinexDiagnostics.ActivitySource.StartActivity(
                "CategoriesController.GetEffectiveSchema",
                ActivityKind.Internal);

            activity?.SetTag("catalog.category_id", id);

            var result = await catalogService.GetEffectiveSchemaAsync(id, ct);

            if (result.IsFailure)
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.Error.Code);
                activity?.SetTag("error.code", result.Error.Code);
                return FromResult(result);
            }

            return FromResult(result, "Effective category schema retrieved successfully.");
        }

        [HttpPost]
        [ProducesResponseType(typeof(ApiResponse<CategoryDetailsDto>), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<ActionResult<ApiResponse<CategoryDetailsDto>>> Create(
            [FromBody] CreateCategoryDto request,
            CancellationToken ct = default)
        {
            using var activity = FinexDiagnostics.ActivitySource.StartActivity(
                "CategoriesController.Create",
                ActivityKind.Internal);

            activity?.SetTag("catalog.category_code", request.Code);

            var result = await catalogService.CreateCategoryAsync(request, ct);
            if (result.IsFailure)
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.Error.Code);
                activity?.SetTag("error.code", result.Error.Code);
                return FromResult(result);
            }

            activity?.SetTag("catalog.category_id", result.Value.Id);

            return CreatedAtAction(
                nameof(GetById),
                new { id = result.Value.Id },
                ApiResponse<CategoryDetailsDto>.Ok(result.Value, "Category created successfully."));
        }

        [HttpPut("{id:int}")]
        [ProducesResponseType(typeof(ApiResponse<CategoryDetailsDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<ActionResult<ApiResponse<CategoryDetailsDto>>> Update(
            int id,
            [FromBody] UpdateCategoryDto request,
            CancellationToken ct = default)
        {
            using var activity = FinexDiagnostics.ActivitySource.StartActivity(
                "CategoriesController.Update",
                ActivityKind.Internal);

            activity?.SetTag("catalog.category_id", id);
            activity?.SetTag("catalog.force_breaking_changes", request.ForceBreakingChanges);

            var result = await catalogService.UpdateCategoryAsync(id, request, ct);

            if (result.IsFailure)
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.Error.Code);
                activity?.SetTag("error.code", result.Error.Code);
                return FromResult(result);
            }

            return FromResult(result, "Category updated successfully.");
        }

        [HttpPatch("{id:int}/move")]
        [ProducesResponseType(typeof(ApiResponse<CategoryDetailsDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<ActionResult<ApiResponse<CategoryDetailsDto>>> Move(
            int id,
            [FromBody] MoveCategoryDto request,
            CancellationToken ct = default)
        {
            using var activity = FinexDiagnostics.ActivitySource.StartActivity(
                "CategoriesController.Move",
                ActivityKind.Internal);

            activity?.SetTag("catalog.category_id", id);
            activity?.SetTag("catalog.new_parent_id", request.NewParentCategoryId);

            var result = await catalogService.MoveCategoryAsync(id, request, ct);

            if (result.IsFailure)
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.Error.Code);
                activity?.SetTag("error.code", result.Error.Code);
                return FromResult(result);
            }

            return FromResult(result, "Category moved successfully.");
        }

        [HttpPatch("{id:int}/status")]
        [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<ActionResult<ApiResponse>> SetStatus(
            int id,
            [FromBody] SetCategoryStatusDto request,
            CancellationToken ct = default)
        {
            using var activity = FinexDiagnostics.ActivitySource.StartActivity(
                "CategoriesController.SetStatus",
                ActivityKind.Internal);

            activity?.SetTag("catalog.category_id", id);
            activity?.SetTag("catalog.target_is_active", request.IsActive);

            var result = request.IsActive
                ? await catalogService.ActivateCategoryAsync(id, ct)
                : await catalogService.DeactivateCategoryAsync(id, ct);

            if (result.IsFailure)
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.Error.Code);
                activity?.SetTag("error.code", result.Error.Code);
                return FromResult(result);
            }

            var message = request.IsActive
                ? "Category activated successfully."
                : "Category deactivated successfully.";

            return FromResult(result, message);
        }

        [HttpPatch("{id:int}/activate")]
        [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<ActionResult<ApiResponse>> Activate(
            int id,
            CancellationToken ct = default)
        {
            using var activity = FinexDiagnostics.ActivitySource.StartActivity(
                "CategoriesController.Activate",
                ActivityKind.Internal);

            activity?.SetTag("catalog.category_id", id);

            var result = await catalogService.ActivateCategoryAsync(id, ct);

            if (result.IsFailure)
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.Error.Code);
                activity?.SetTag("error.code", result.Error.Code);
                return FromResult(result);
            }

            return FromResult(result, "Category activated successfully.");
        }

        [HttpPatch("{id:int}/deactivate")]
        [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<ActionResult<ApiResponse>> Deactivate(
            int id,
            CancellationToken ct = default)
        {
            using var activity = FinexDiagnostics.ActivitySource.StartActivity(
                "CategoriesController.Deactivate",
                ActivityKind.Internal);

            activity?.SetTag("catalog.category_id", id);

            var result = await catalogService.DeactivateCategoryAsync(id, ct);

            if (result.IsFailure)
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.Error.Code);
                activity?.SetTag("error.code", result.Error.Code);
                return FromResult(result);
            }

            return FromResult(result, "Category deactivated successfully.");
        }

        [HttpDelete("{id:int}")]
        [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<ActionResult<ApiResponse>> Delete(
            int id,
            CancellationToken ct = default)
        {
            using var activity = FinexDiagnostics.ActivitySource.StartActivity(
                "CategoriesController.Delete",
                ActivityKind.Internal);

            activity?.SetTag("catalog.category_id", id);

            var result = await catalogService.DeleteCategoryAsync(id, ct);

            if (result.IsFailure)
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.Error.Code);
                activity?.SetTag("error.code", result.Error.Code);
                return FromResult(result);
            }

            return FromResult(result, "Category deleted successfully.");
        }
    }
}