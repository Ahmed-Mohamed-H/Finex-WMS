using System;
using System.Collections.Generic;
using System.Text;

namespace Finex.Domain.Entities
{
    public class ProducedLineSsccBarcode
    {
        public int Id { get; set; }
        public string SsccBarcode { get; set; } = string.Empty;

        public int ProducedLineId { get; set; }
        public ProducedLine ProducedLine { get; set; } = null!;
    }
}
