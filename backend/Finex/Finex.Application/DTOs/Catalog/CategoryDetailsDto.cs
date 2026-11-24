using System;
using System.Collections.Generic;
using System.Text;

namespace Finex.Application.DTOs.Catalog
{
    public sealed record CategoryDetailsDto(
     int Id,
     string Code,
     string Name,
     string? Description,
     int Level,
     bool IsActive,
     int? ParentCategoryId,
     string? ParentCategoryCode,
     string? ParentCategoryName,
     int SubCategoriesCount,
     DateTimeOffset CreatedAtUtc,
     DateTimeOffset? UpdatedAtUtc);
}
