namespace Finex.Application.DTOs.Catalog;

public sealed record CreateCategoryDto(
    string Code,
    string Name,
    string? Description = null,
    int? ParentCategoryId = null,
    IReadOnlyList<AttributeDefinitionDto>? Attributes = null);