using System;
using System.Collections.Generic;
using System.Text;

namespace Finex.Application.DTOs.Production
{
    public sealed record ProducedLineSummaryDto(
     int Id,
     string Sku,
     string UnitName,
     string UnitBarcode,
     int Quantity,
     DateOnly ProductionDate,
     DateOnly ExpiryDate);
}
