using Finex.Domain.Enums.Catalog;
using Finex.Domain.ValueObjects.Catalog;

namespace Finex.Domain.Entities;

public sealed class Product
{
    public int Id { get; set; }

    public string Sku { get; set; } = string.Empty;
    public string? UnitBarcode { get; set; }
    public string? CartonBarcode { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public int CategoryId { get; set; }
    public ProductCategory Category { get; set; } = null!;

    public PhysicalProperties PhysicalProperties { get; set; } = null!;

    public Dictionary<string, object?> TechnicalSpecs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public TrackingMode TrackingMode { get; set; } = TrackingMode.BatchOnly;
    public int? ShelfLifeDays { get; set; }
    public int? MaxStackLayers { get; set; }
    public int? MinStockLevel { get; set; }
    public ProductStatus Status { get; set; } = ProductStatus.Draft;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public bool CanReceiveFromProduction => Status == ProductStatus.Active;
    public bool CanDispatchFromWarehouse => Status is ProductStatus.Active or ProductStatus.Discontinued;
}