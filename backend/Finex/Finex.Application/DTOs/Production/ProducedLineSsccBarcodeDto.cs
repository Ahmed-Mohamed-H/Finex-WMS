namespace Finex.Application.DTOs.Production
{
    public sealed record ProducedLineSsccBarcodeDto(
     int Id,
     string SsccBarcode,
     bool IsActive);
}