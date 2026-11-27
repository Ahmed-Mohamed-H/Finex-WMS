namespace Finex.Application.DTOs.Catalog;

public sealed record UpdateCategoryDto(
    string Name,
    string? Description = null,
    IReadOnlyList<AttributeDefinitionDto>? Attributes = null)
{
    public bool ForceBreakingChanges { get; init; }
}