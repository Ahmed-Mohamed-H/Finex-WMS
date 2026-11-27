using Finex.Application.DTOs.Catalog;
using Finex.Domain.Entities;
using Riok.Mapperly.Abstractions;

namespace Finex.Application.Mappings.Catalog;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class CategoryQueryMapper
{
    public static partial IQueryable<CategoryFlatNodeDto> ProjectToFlatNodes(
        this IQueryable<ProductCategory> query);

    [MapperIgnoreTarget(nameof(CategoryDetailsDto.OwnAttributes))]
    [MapperIgnoreTarget(nameof(CategoryDetailsDto.EffectiveSchema))]
    public static partial IQueryable<CategoryDetailsDto> ProjectToDetailsDto(
        this IQueryable<ProductCategory> query);
}