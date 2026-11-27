namespace Finex.Domain.Enums.Catalog
{
    public enum PackagingType
    {
        Box = 1,          // Individual retail box
        Carton = 2,       // Master shipping carton
        ShrinkWrap = 3,   // Heat-shrink wrap
        Bale = 4,         // Compressed bale
        SingleRoll = 5,   // Single roll (jumbo, industrial, or center-pull)
        Bag = 6,          // Bag, flexible pouch, or refill pack
        Pallet = 7,       // Full pallet (bulk handling unit)
        Bundle = 8,       // Multi-unit bundle or pack (e.g., 4 or 6 rolls)
        Unit = 9          // Single device or dispenser unit
    }
}