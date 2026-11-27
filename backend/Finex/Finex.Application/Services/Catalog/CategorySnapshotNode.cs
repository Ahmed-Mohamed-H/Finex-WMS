using Finex.Domain.ValueObjects.Catalog;
using System;
using System.Collections.Generic;
using System.Text;

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
