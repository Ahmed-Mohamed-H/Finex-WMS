using Finex.Domain.Enums.Catalog;
using Finex.Domain.ValueObjects.Catalog;
using Xunit;

namespace Finex.UnitTests.Catalog;

public sealed class CategorySchemaDomainTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a")] // أقل من حرفين
    [InlineData("1plyCount")] // يبدأ برقم
    [InlineData("ply.count")] // نقطة تكسر SQL JSON path
    [InlineData("ply-count")] // شرطة غير مسموحة
    [InlineData("ply$count")] // رمز غير مسموح
    [InlineData("عدد_الطبقات")] // حروف غير إنجليزية
    public void AttributeDefinition_ShouldThrow_WhenKeyIsInvalidOrUnsafeForSqlJson(string invalidKey)
    {
        var act = () => new CategoryAttributeDefinition(
            invalidKey,
            "Valid Label",
            AttributeDataType.String,
            isRequired: true);

        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void AttributeDefinition_ShouldRejectAllowedValues_WhenDataTypeIsNotString()
    {
        var nonStringType = Enum.GetValues<AttributeDataType>()
            .First(t => t != AttributeDataType.String);

        var act = () => new CategoryAttributeDefinition(
            "plyCount",
            "عدد الطبقات",
            nonStringType,
            isRequired: true,
            allowedValues: ["1", "2"]);

        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void AttributeDefinition_ShouldNormalizeAndDeduplicateAllowedValues()
    {
        var def = new CategoryAttributeDefinition(
            "  foldType  ",
            "  نوع الطي  ",
            AttributeDataType.String,
            isRequired: true,
            unit: "  نوع  ",
            allowedValues: ["Z-Fold", " z-fold ", "C-Fold", "  "]);

        Assert.Equal("foldType", def.Key);
        Assert.Equal("نوع الطي", def.Label);
        Assert.Equal("نوع", def.Unit);
        Assert.Equal(["Z-Fold", "C-Fold"], def.AllowedValues);
    }

    [Fact]
    public void AttributeSchema_ShouldThrow_WhenDuplicateKeysExistCaseInsensitively()
    {
        var attr1 = new CategoryAttributeDefinition("plyCount", "Label 1", AttributeDataType.String, true);
        var attr2 = new CategoryAttributeDefinition("PLYCOUNT", "Label 2", AttributeDataType.String, false);

        var act = () => new CategoryAttributeSchema([attr1, attr2]);

        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void AttributeSchema_ShouldThrow_WhenExceedingMax30Attributes()
    {
        var tooMany = Enumerable.Range(1, 31)
            .Select(i => new CategoryAttributeDefinition($"attr_{i}", $"Label {i}", AttributeDataType.String, false))
            .ToList();

        var act = () => new CategoryAttributeSchema(tooMany);

        Assert.Throws<ArgumentException>(act);
    }
}