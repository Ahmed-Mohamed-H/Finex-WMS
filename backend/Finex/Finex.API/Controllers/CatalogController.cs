using Finex.API.Contracts.Catalog;
using Finex.API.Contracts.Common;
using Finex.Application.DTOs.Catalog;
using Finex.Application.Interfaces.Catalog;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Finex.API.Controllers;

[Route("api/catalog/categories")]
public sealed class CatalogController(
    ICatalogService catalogService,
    IValidator<CreateCategoryDto> createValidator,
    IValidator<UpdateCategoryDto> updateValidator,
    IValidator<MoveCategoryDto> moveValidator) : ApiControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<CategoryDetailsDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<CategoryDetailsDto>>> CreateCategory(
        [FromBody] CreateCategoryRequest request,
        CancellationToken ct)
    {
        var dto = new CreateCategoryDto(
            request.Code,
            request.Name,
            request.Description,
            request.ParentCategoryId);

        var validation = await createValidator.ValidateAsync(dto, ct);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        }

        var result = await catalogService.CreateCategoryAsync(dto, ct);
        if (!result.IsSuccess)
        {
            return FromResult(result);
        }

        return CreatedAtAction(
            nameof(GetCategoryById),
            new { id = result.Value.Id },
            ApiResponse<CategoryDetailsDto>.Ok(result.Value, "Category created successfully."));
    }

    [HttpGet("tree")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CategoryTreeNodeDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CategoryTreeNodeDto>>>> GetCategoryTree(
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        var result = await catalogService.GetCategoryTreeAsync(includeInactive, ct);
        return FromResult(result);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<CategoryDetailsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<CategoryDetailsDto>>> GetCategoryById(
        int id,
        CancellationToken ct)
    {
        if (id <= 0)
        {
            return InvalidRouteIdProblem();
        }

        var result = await catalogService.GetCategoryByIdAsync(id, ct);
        return FromResult(result);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<CategoryDetailsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<CategoryDetailsDto>>> UpdateCategory(
        int id,
        [FromBody] UpdateCategoryRequest request,
        CancellationToken ct)
    {
        if (id <= 0)
        {
            return InvalidRouteIdProblem();
        }

        var dto = new UpdateCategoryDto(request.Name, request.Description);

        var validation = await updateValidator.ValidateAsync(dto, ct);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        }

        var result = await catalogService.UpdateCategoryAsync(id, dto, ct);
        return FromResult(result, "Category updated successfully.");
    }

    [HttpPatch("{id:int}/move")]
    [ProducesResponseType(typeof(ApiResponse<CategoryDetailsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<CategoryDetailsDto>>> MoveCategory(
        int id,
        [FromBody] MoveCategoryRequest request,
        CancellationToken ct)
    {
        if (id <= 0)
        {
            return InvalidRouteIdProblem();
        }

        var dto = new MoveCategoryDto(request.NewParentCategoryId);

        var validation = await moveValidator.ValidateAsync(dto, ct);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        }

        var result = await catalogService.MoveCategoryAsync(id, dto, ct);
        return FromResult(result, "Category moved successfully.");
    }

    [HttpPatch("{id:int}/deactivate")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse>> DeactivateCategory(int id, CancellationToken ct)
    {
        if (id <= 0)
        {
            return InvalidRouteIdProblem();
        }

        var result = await catalogService.DeactivateCategoryAsync(id, ct);
        return FromResult(result, "Category deactivated successfully.");
    }

    [HttpPatch("{id:int}/activate")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse>> ActivateCategory(int id, CancellationToken ct)
    {
        if (id <= 0)
        {
            return InvalidRouteIdProblem();
        }

        var result = await catalogService.ActivateCategoryAsync(id, ct);
        return FromResult(result, "Category activated successfully.");
    }

    private ActionResult InvalidRouteIdProblem()
    {
        return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
        {
            ["id"] = ["Route parameter 'id' must be a positive integer."]
        }));
    }
}