using Finex.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finex.Infrastructure.Persistence.Configurations.Categories
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
        }
    }
}