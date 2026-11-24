namespace Finex.API.Contracts.Catalog
{
    public sealed record CreateCategoryRequest(
      string Code,
      string Name,
      string? Description,
      int? ParentCategoryId);
}
