using System;
using System.Collections.Generic;
using System.Text;

namespace Finex.Application.DTOs
{
    public sealed class ProducedLineDto
    {
        public string Sku { get; set; } = string.Empty;
        public string UnitName { get; set; } = string.Empty;
        public string UnitBarcode { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public DateOnly ProductionDate { get; set; }
        public DateOnly ExpiryDate { get; set; }

        public IReadOnlyList<string> SsccBarcodes { get; set; } = [];
    }
}
