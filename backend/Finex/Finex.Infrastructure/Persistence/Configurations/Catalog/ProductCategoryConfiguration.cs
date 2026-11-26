using Finex.Domain.Entities;
using Finex.Domain.ValueObjects.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Finex.Infrastructure.Persistence.Configurations.Catalog
{
    public sealed class ProductCategoryConfiguration : IEntityTypeConfiguration<ProductCategory>
    {
        public void Configure(EntityTypeBuilder<ProductCategory> builder)
        {
            builder.ToTable("ProductCategories", tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "CK_ProductCategories_NoSelfParent",
                    "[ParentCategoryId] IS NULL OR [ParentCategoryId] <> [Id]");

                tableBuilder.HasCheckConstraint(
                    "CK_ProductCategories_ValidLevel",
                    "([ParentCategoryId] IS NULL AND [Level] = 1) OR ([ParentCategoryId] IS NOT NULL AND [Level] >= 2)");
            });

            builder.HasKey(c => c.Id);

            builder.Property(c => c.Code)
                 .IsRequired()
                 .IsUnicode(false)
                 .HasMaxLength(50);

            builder.Property(c => c.Name)
                .IsRequired()
                .HasMaxLength(150);

            builder.Property(c => c.Description)
                .HasMaxLength(500);

            builder.Property(c => c.Level)
                .IsRequired();

            builder.Property(c => c.IsActive)
                .IsRequired();

            builder.Property(c => c.CreatedAtUtc)
                .IsRequired();

            builder.Property(c => c.UpdatedAtUtc);

            builder.HasIndex(c => c.Code)
                .IsUnique()
                .HasDatabaseName("UX_ProductCategories_Code");

            builder.HasIndex(c => c.Name)
                .IsUnique()
                .HasFilter("[ParentCategoryId] IS NULL")
                .HasDatabaseName("UX_ProductCategories_RootName");

            builder.HasIndex(c => new { c.ParentCategoryId, c.Name })
                .IsUnique()
                .HasFilter("[ParentCategoryId] IS NOT NULL")
                .HasDatabaseName("UX_ProductCategories_ParentId_Name");

            builder.HasIndex(c => c.ParentCategoryId)
                .HasDatabaseName("IX_ProductCategories_ParentCategoryId");

            builder.HasIndex(c => new { c.IsActive, c.Level, c.ParentCategoryId })
                .HasFilter("[IsActive] = 1")
                .HasDatabaseName("IX_ProductCategories_ActiveHierarchy");

            builder.HasOne(c => c.ParentCategory)
                .WithMany(c => c.SubCategories)
                .HasForeignKey(c => c.ParentCategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            var schemaJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                Converters = { new JsonStringEnumConverter() }
            };

            var schemaConverter = new ValueConverter<CategoryAttributeSchema?, string?>(
                v => v == null ? null : JsonSerializer.Serialize(v, schemaJsonOptions),
                v => string.IsNullOrWhiteSpace(v) ? null : JsonSerializer.Deserialize<CategoryAttributeSchema>(v, schemaJsonOptions));

            var schemaComparer = new ValueComparer<CategoryAttributeSchema?>(
                (a, b) => JsonSerializer.Serialize(a, schemaJsonOptions) == JsonSerializer.Serialize(b, schemaJsonOptions),
                v => v == null ? 0 : JsonSerializer.Serialize(v, schemaJsonOptions).GetHashCode(),
                v => v == null ? null : JsonSerializer.Deserialize<CategoryAttributeSchema>(JsonSerializer.Serialize(v, schemaJsonOptions), schemaJsonOptions));

            builder.Property(c => c.AttributeSchema)
               .HasColumnType("json")
               .HasConversion(schemaConverter, schemaComparer)
               .IsRequired(false);
        }
    }
}