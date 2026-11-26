using System.Text.Json;
using Finex.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Finex.Infrastructure.Persistence.Configurations.Catalog;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    private static readonly JsonSerializerOptions SpecsJsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", table =>
        {
            table.HasCheckConstraint(
                "CK_Products_ValidWeights",
                "[NetWeightGrams] > 0 AND [GrossWeightGrams] >= [NetWeightGrams]");

            table.HasCheckConstraint(
                "CK_Products_ValidDimensions",
                "[LengthCm] > 0 AND [WidthCm] > 0 AND [HeightCm] > 0");

            table.HasCheckConstraint(
                "CK_Products_ValidPackCapacity",
                "[PackCapacity] >= 1");

            table.HasCheckConstraint(
                "CK_Products_ValidShelfLife",
                "([TrackingMode] <> 'BatchAndExpiry' AND ([ShelfLifeDays] IS NULL OR [ShelfLifeDays] > 0)) OR ([TrackingMode] = 'BatchAndExpiry' AND [ShelfLifeDays] > 0)");

            table.HasCheckConstraint(
                "CK_Products_ValidMaxStackLayers",
                "[MaxStackLayers] IS NULL OR [MaxStackLayers] >= 1");

            table.HasCheckConstraint(
                "CK_Products_ValidMinStockLevel",
                "[MinStockLevel] IS NULL OR [MinStockLevel] >= 0");

            table.HasCheckConstraint(
                "CK_Products_DistinctBarcodes",
                "[UnitBarcode] IS NULL OR [CartonBarcode] IS NULL OR [UnitBarcode] <> [CartonBarcode]");
        });

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Sku).HasMaxLength(64).IsRequired();
        builder.Property(p => p.UnitBarcode).HasMaxLength(64).IsRequired(false);
        builder.Property(p => p.CartonBarcode).HasMaxLength(64).IsRequired(false);
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(1000).IsRequired(false);

        builder.Property(p => p.TrackingMode).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(p => p.ShelfLifeDays).IsRequired(false);
        builder.Property(p => p.MaxStackLayers).IsRequired(false);
        builder.Property(p => p.MinStockLevel).IsRequired(false);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(30).IsRequired();

        builder.Property(p => p.CreatedAtUtc).IsRequired();
        builder.Property(p => p.UpdatedAtUtc).IsRequired(false);
        builder.Property(p => p.RowVersion).IsRowVersion();

        builder.HasOne(p => p.Category)
            .WithMany()
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.OwnsOne(p => p.PhysicalProperties, phys =>
        {
            phys.Property(x => x.GrossWeightGrams).HasColumnName("GrossWeightGrams").HasPrecision(12, 2).IsRequired();
            phys.Property(x => x.NetWeightGrams).HasColumnName("NetWeightGrams").HasPrecision(12, 2).IsRequired();
            phys.Property(x => x.LengthCm).HasColumnName("LengthCm").HasPrecision(10, 2).IsRequired();
            phys.Property(x => x.WidthCm).HasColumnName("WidthCm").HasPrecision(10, 2).IsRequired();
            phys.Property(x => x.HeightCm).HasColumnName("HeightCm").HasPrecision(10, 2).IsRequired();
            phys.Property(x => x.VolumeCm3)
                .HasColumnName("VolumeCm3")
                .HasPrecision(18, 2)
                .HasComputedColumnSql("ROUND([LengthCm] * [WidthCm] * [HeightCm], 2)", stored: true)
                .ValueGeneratedOnAddOrUpdate();
            phys.Property(x => x.PackagingType).HasColumnName("PackagingType").HasConversion<string>().HasMaxLength(30).IsRequired();
            phys.Property(x => x.PackCapacity).HasColumnName("PackCapacity").IsRequired();
            phys.Property(x => x.StorageZone).HasColumnName("StorageZone").HasConversion<string>().HasMaxLength(30).IsRequired();
        });

        builder.Navigation(p => p.PhysicalProperties).IsRequired();

        var specsConverter = new ValueConverter<Dictionary<string, object?>, string>(
            v => JsonSerializer.Serialize(v, SpecsJsonOptions),
            v => DeserializeSpecs(v));

        var specsComparer = new ValueComparer<Dictionary<string, object?>>(
            (a, b) => JsonSerializer.Serialize(a, SpecsJsonOptions) == JsonSerializer.Serialize(b, SpecsJsonOptions),
            v => JsonSerializer.Serialize(v, SpecsJsonOptions).GetHashCode(),
            v => DeserializeSpecs(JsonSerializer.Serialize(v, SpecsJsonOptions)));

        builder.Property(p => p.TechnicalSpecs)
            .HasColumnType("json")
            .HasConversion(specsConverter, specsComparer)
            .IsRequired();

        builder.HasIndex(p => p.Sku)
            .IsUnique()
            .HasDatabaseName("UX_Products_Sku");

        builder.HasIndex(p => p.UnitBarcode)
            .IsUnique()
            .HasFilter("[UnitBarcode] IS NOT NULL")
            .HasDatabaseName("UX_Products_UnitBarcode");

        builder.HasIndex(p => p.CartonBarcode)
            .IsUnique()
            .HasFilter("[CartonBarcode] IS NOT NULL")
            .HasDatabaseName("UX_Products_CartonBarcode");

        builder.HasIndex(p => new { p.CategoryId, p.Status })
            .HasDatabaseName("IX_Products_CategoryId_Status");
    }

    private static Dictionary<string, object?> DeserializeSpecs(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, SpecsJsonOptions)
                  ?? new Dictionary<string, JsonElement>();

        var result = new Dictionary<string, object?>(raw.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, element) in raw)
        {
            result[key] = element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDecimal(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            };
        }

        return result;
    }
}