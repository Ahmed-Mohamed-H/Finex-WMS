using Finex.Domain.ValueObjects.Catalog;

namespace Finex.Application.Services.Catalog
{
    public sealed record CategorySnapshotNode(
     int Id,
     string Code,
     string Name,
     string? Description,
     int Level,
     bool IsActive,
     int? ParentCategoryId,
     CategoryAttributeSchema? AttributeSchema);
}