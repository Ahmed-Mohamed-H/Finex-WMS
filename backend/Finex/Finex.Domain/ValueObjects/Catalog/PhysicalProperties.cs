using Finex.Domain.Enums.Catalog;
namespace Finex.Domain.ValueObjects.Catalog;

public sealed record PhysicalProperties
{
    public decimal GrossWeightGrams { get; private set; }
    public decimal NetWeightGrams { get; private set; }

    public decimal LengthCm { get; private set; }
    public decimal WidthCm { get; private set; }
    public decimal HeightCm { get; private set; }
    public decimal VolumeCm3 { get; private set; }

    public PackagingType PackagingType { get; private set; }
    public int PackCapacity { get; private set; }
    public StorageZoneType StorageZone { get; private set; }

    private PhysicalProperties() { }

    public PhysicalProperties(
        decimal grossWeightGrams,
        decimal netWeightGrams,
        decimal lengthCm,
        decimal widthCm,
        decimal heightCm,
        PackagingType packagingType,
        int packCapacity,
        StorageZoneType storageZone)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(netWeightGrams);
        ArgumentOutOfRangeException.ThrowIfLessThan(grossWeightGrams, netWeightGrams);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lengthCm);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(widthCm);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightCm);
        ArgumentOutOfRangeException.ThrowIfLessThan(packCapacity, 1);

        if (!Enum.IsDefined(packagingType))
            throw new ArgumentOutOfRangeException(nameof(packagingType), "Invalid packaging type.");

        if (!Enum.IsDefined(storageZone))
            throw new ArgumentOutOfRangeException(nameof(storageZone), "Invalid storage zone.");

        GrossWeightGrams = grossWeightGrams;
        NetWeightGrams = netWeightGrams;
        LengthCm = lengthCm;
        WidthCm = widthCm;
        HeightCm = heightCm;
        VolumeCm3 = Math.Round(lengthCm * widthCm * heightCm, 2);
        PackagingType = packagingType;
        PackCapacity = packCapacity;
        StorageZone = storageZone;
    }
}