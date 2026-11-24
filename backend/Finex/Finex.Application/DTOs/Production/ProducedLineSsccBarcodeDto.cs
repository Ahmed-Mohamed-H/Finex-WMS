using System;
using System.Collections.Generic;
using System.Text;

namespace Finex.Application.DTOs.Production
{
    public sealed record ProducedLineSsccBarcodeDto(
     int Id,
     string SsccBarcode,
     bool IsActive);
}
