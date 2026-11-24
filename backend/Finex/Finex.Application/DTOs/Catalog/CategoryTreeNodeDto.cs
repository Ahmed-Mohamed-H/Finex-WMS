using System;
using System.Collections.Generic;
using System.Text;

namespace Finex.Application.DTOs.Catalog
{
    public sealed record CategoryTreeNodeDto(
      int Id,
      string Code,
      string Name,
      string? Description,
      int Level,
      bool IsActive,
      int? ParentCategoryId,
      IReadOnlyList<CategoryTreeNodeDto> Children);
}
