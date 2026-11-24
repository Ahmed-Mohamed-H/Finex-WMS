using System;
using System.Collections.Generic;
using System.Text;

namespace Finex.Application.DTOs.Catalog
{
    public sealed record CreateCategoryDto(
      string Code,
      string Name,
      string? Description,
      int? ParentCategoryId);
}
