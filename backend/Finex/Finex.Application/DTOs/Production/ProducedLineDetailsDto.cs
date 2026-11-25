namespace Finex.Application.DTOs.Production
{
    public sealed record ProducedLineDetailsDto(
    int Id,
    string Sku,
    string UnitName,
    string UnitBarcode,
    int Quantity,
    DateOnly ProductionDate,
    DateOnly ExpiryDate,
    IReadOnlyList<ProducedLineSsccBarcodeDto> SsccBarcodes);
}