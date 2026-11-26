using Finex.Domain.ValueObjects.Catalog;

namespace Finex.Domain.Entities
{
    public sealed class ProductCategory
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }

        public int Level { get; set; } = 1;

        public bool IsActive { get; set; } = true;
        public CategoryAttributeSchema? AttributeSchema { get; set; }
        public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? UpdatedAtUtc { get; set; }

        public int? ParentCategoryId { get; set; }
        public ProductCategory? ParentCategory { get; set; }
        public ICollection<ProductCategory> SubCategories { get; set; } = [];
    }
}