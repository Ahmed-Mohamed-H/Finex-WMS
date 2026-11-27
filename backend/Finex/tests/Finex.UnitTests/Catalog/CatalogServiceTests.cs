using Finex.Application.DTOs.Catalog;
using Finex.Application.Interfaces;
using Finex.Application.Interfaces.Catalog;
using Finex.Application.Services.Catalog;
using Finex.Domain.Entities;
using Finex.Domain.Enums.Catalog;
using Finex.Domain.ValueObjects.Catalog;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Finex.UnitTests.Catalog;

public sealed class CatalogServiceTests
{
    private readonly List<ProductCategory> _categories = [];
    private readonly List<Product> _products = [];
    private readonly IFinexDbContext _dbContext;
    private readonly ICategoryCatalogCache _cache;
    private readonly CatalogService _sut;

    public CatalogServiceTests()
    {
        var categoriesDbSet = AsyncQueryableTestHelper.CreateMockDbSet(_categories);
        var productsDbSet = AsyncQueryableTestHelper.CreateMockDbSet(_products);

        _dbContext = Substitute.For<IFinexDbContext>();
        _dbContext.ProductCategories.Returns(categoriesDbSet);
        _dbContext.Products.Returns(productsDbSet);

        _dbContext.ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task>>()(call.Arg<CancellationToken>()));

        _cache = Substitute.For<ICategoryCatalogCache>();
        _cache.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(_ => CategoryCatalogSnapshot.Create(
                _categories.Select(c => new CategorySnapshotNode(
                    c.Id, c.Code, c.Name, c.Description, c.Level, c.IsActive, c.ParentCategoryId, c.AttributeSchema))
                .ToList()));

        _sut = new CatalogService(_dbContext, _cache, NullLogger<CatalogService>.Instance);
    }

    [Fact]
    public async Task CreateCategory_ShouldFail_WhenRootCategoryHasNoAttributes()
    {
        var dto = new CreateCategoryDto("TISSUE", "مناديل", Attributes: []);

        var result = await _sut.CreateCategoryAsync(dto);

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.RootSchemaRequired", result.Error.Code);
    }

    [Fact]
    public async Task CreateCategory_ShouldFail_WhenSubcategoryDefinesOwnAttributes()
    {
        _categories.Add(new ProductCategory { Id = 1, Code = "TISSUE", Name = "مناديل", Level = 1, IsActive = true });

        var dto = new CreateCategoryDto(
            "FACIAL",
            "مناديل وجه",
            ParentCategoryId: 1,
            Attributes: [new AttributeDefinitionDto("extra", "إضافي", AttributeDataType.String, false)]);

        var result = await _sut.CreateCategoryAsync(dto);

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.SubCategorySchemaForbidden", result.Error.Code);
    }

    [Fact]
    public async Task UpdateCategory_ShouldReject_WhenCategoryIsInactive()
    {
        _categories.Add(new ProductCategory
        {
            Id = 1,
            Code = "TISSUE",
            Name = "مناديل",
            Level = 1,
            IsActive = false,
            AttributeSchema = new CategoryAttributeSchema([
                new CategoryAttributeDefinition("ply", "طبقات", AttributeDataType.String, true)
            ])
        });

        var dto = new UpdateCategoryDto(
            "اسم جديد",
            Attributes: [new AttributeDefinitionDto("ply", "طبقات", AttributeDataType.String, true)]);

        var result = await _sut.UpdateCategoryAsync(1, dto);

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.InactiveCategoryLocked", result.Error.Code);
    }

    [Fact]
    public async Task UpdateCategory_ShouldAllowDeletingUnusedAttribute_WithoutForceFlag()
    {
        var schema = new CategoryAttributeSchema([
            new CategoryAttributeDefinition("ply", "طبقات", AttributeDataType.String, true),
            new CategoryAttributeDefinition("unusedAttr", "صفة غير مستخدمة", AttributeDataType.String, false)
        ]);

        var root = new ProductCategory
        {
            Id = 1,
            Code = "TISSUE",
            Name = "مناديل",
            Level = 1,
            IsActive = true,
            AttributeSchema = schema
        };
        _categories.Add(root);
        _products.Add(AsyncQueryableTestHelper.CreateProductWithCategory(1));

        _dbContext.GetCategorySubtreeAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([root]);

        // SQL يؤكد إن 0 منتجات استخدمت unusedAttr
        _dbContext.CountProductsWithAttributeKeyAsync(Arg.Any<IReadOnlyCollection<int>>(), "unusedAttr", Arg.Any<CancellationToken>())
            .Returns(0);

        var dto = new UpdateCategoryDto(
            "مناديل",
            Attributes: [new AttributeDefinitionDto("ply", "طبقات", AttributeDataType.String, true)])
        {
            ForceBreakingChanges = false
        };

        var result = await _sut.UpdateCategoryAsync(1, dto);

        Assert.True(result.IsSuccess);
        await _cache.Received(1).RefreshProactivelyAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateCategory_ShouldBlockDeletingPopulatedAttribute_UnlessForceBreakingChangesIsTrue()
    {
        var schema = new CategoryAttributeSchema([
            new CategoryAttributeDefinition("ply", "طبقات", AttributeDataType.String, true),
            new CategoryAttributeDefinition("foldType", "نوع الطي", AttributeDataType.String, true)
        ]);

        var root = new ProductCategory
        {
            Id = 1,
            Code = "TISSUE",
            Name = "مناديل",
            Level = 1,
            IsActive = true,
            AttributeSchema = schema
        };
        _categories.Add(root);
        _products.Add(AsyncQueryableTestHelper.CreateProductWithCategory(1));

        _dbContext.GetCategorySubtreeAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([root]);

        // SQL يؤكد إن 15 منتج مسجلين بـ foldType
        _dbContext.CountProductsWithAttributeKeyAsync(Arg.Any<IReadOnlyCollection<int>>(), "foldType", Arg.Any<CancellationToken>())
            .Returns(15);

        // المحاولة الأولى بدون تأكيد -> مرفوضة
        var blockedDto = new UpdateCategoryDto(
            "مناديل",
            Attributes: [new AttributeDefinitionDto("ply", "طبقات", AttributeDataType.String, true)])
        {
            ForceBreakingChanges = false
        };

        var blockedResult = await _sut.UpdateCategoryAsync(1, blockedDto);
        Assert.True(blockedResult.IsFailure);
        Assert.Equal("Catalog.SchemaBreakingChange", blockedResult.Error.Code);
        Assert.Contains("15 product(s)", blockedResult.Error.Description);

        // المحاولة التانية بتأكيد صريح -> مقبولة
        var forcedDto = blockedDto with { ForceBreakingChanges = true };
        var forcedResult = await _sut.UpdateCategoryAsync(1, forcedDto);
        Assert.True(forcedResult.IsSuccess);
    }

    [Fact]
    public async Task DeleteCategory_ShouldHardDelete_WhenCategoryHasNoChildrenOrProducts()
    {
        _categories.Add(new ProductCategory { Id = 10, Code = "TEMP", Name = "مؤقتة", Level = 1, IsActive = true });

        var result = await _sut.DeleteCategoryAsync(10);

        Assert.True(result.IsSuccess);
        Assert.Empty(_categories);
        await _cache.Received(1).RefreshProactivelyAsync(Arg.Any<CancellationToken>());
    }
}