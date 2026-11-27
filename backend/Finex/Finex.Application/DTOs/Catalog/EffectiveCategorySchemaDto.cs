namespace Finex.Application.DTOs.Catalog;

public sealed record EffectiveCategorySchemaDto(
    int CategoryId,
    string CategoryCode,
    string CategoryName,
    int SourceRootCategoryId,
    string SourceRootCategoryCode,
    string SourceRootCategoryName,
    IReadOnlyList<AttributeDefinitionDto> Attributes);