using Finex.Application.Services.Catalog;
using Finex.Domain.Enums.Catalog;
using Finex.Domain.ValueObjects.Catalog;
using Xunit;

namespace Finex.UnitTests.Catalog;

public sealed class CategoryCatalogSnapshotTests
{
    [Fact]
    public void Create_ShouldResolveInheritedSchemaAcrossThreeLevels_InO1()
    {
        var rootSchema = new CategoryAttributeSchema([
            new CategoryAttributeDefinition("plyCount", "عدد الطبقات", AttributeDataType.String, true)
        ]);

        var nodes = new List<CategorySnapshotNode>
        {
            new(1, "TISSUE", "مناديل", null, 1, true, null, rootSchema),
            new(2, "FACIAL", "مناديل وجه", null, 2, true, 1, null),
            new(3, "FACIAL-LUX", "مناديل وجه فاخرة", null, 3, true, 2, null)
        };

        var snapshot = CategoryCatalogSnapshot.Create(nodes);

        Assert.True(snapshot.IdByCode.TryGetValue("facial-lux", out var resolvedId));
        Assert.Equal(3, resolvedId);

        var effective = snapshot.EffectiveSchemaById[3];
        Assert.NotNull(effective);
        Assert.Equal(1, effective.SourceRootCategoryId);
        Assert.Equal("TISSUE", effective.SourceRootCategoryCode);
        Assert.Single(effective.Attributes);
        Assert.Equal("plyCount", effective.Attributes[0].Key);
    }

    [Fact]
    public void Create_ShouldExcludeInactiveSubtreeFromActiveTree_WhileKeepingItInAllTree()
    {
        var rootSchema = new CategoryAttributeSchema([
            new CategoryAttributeDefinition("grade", "الدرجة", AttributeDataType.String, true)
        ]);

        var nodes = new List<CategorySnapshotNode>
        {
            new(1, "ROOT", "رئيسي", null, 1, true, null, rootSchema),
            new(2, "INACTIVE-PARENT", "فرع معطل", null, 2, false, 1, null),
            new(3, "ORPHAN-CHILD", "حفيد تحت معطل", null, 3, true, 2, null)
        };

        var snapshot = CategoryCatalogSnapshot.Create(nodes);

        Assert.Single(snapshot.ActiveTree);
        Assert.Empty(snapshot.ActiveTree[0].SubCategories);

        Assert.Single(snapshot.AllTree);
        Assert.Single(snapshot.AllTree[0].SubCategories);
        Assert.Single(snapshot.AllTree[0].SubCategories[0].SubCategories);
    }
}