using Finex.Application.DTOs.Catalog;
using Finex.Domain.Entities;
using Riok.Mapperly.Abstractions;

namespace Finex.Application.Mappings.Catalog
{
    [Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Source)]
    public static partial class CategoryCommandMapper
    {
        [MapValue(nameof(ProductCategory.IsActive), true)]
        public static partial ProductCategory ToEntity(this CreateCategoryDto dto);

        private static string TrimString(string value) => value.Trim();

        private static string? TrimNullableString(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}