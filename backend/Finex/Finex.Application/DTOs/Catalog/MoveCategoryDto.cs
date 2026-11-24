using System;
using System.Collections.Generic;
using System.Text;

namespace Finex.Application.DTOs.Catalog
{
    public sealed record MoveCategoryDto(int? NewParentCategoryId);
}
