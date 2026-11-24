using System;
using System.Collections.Generic;
using System.Text;

namespace Finex.Application.DTOs
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
