namespace Finex.API.Contracts.Catalog
{
    public sealed record UpdateCategoryRequest(
    string Name,
    string? Description);
}