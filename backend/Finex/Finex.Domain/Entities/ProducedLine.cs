namespace Finex.Domain.Entities
{
    public class ProducedLine
    {
        public int Id { get; set; }
        public string Sku { get; set; } = string.Empty;
        public string UnitName { get; set; } = string.Empty;
        public string UnitBarcode { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public DateOnly ProductionDate { get; set; }
        public DateOnly ExpiryDate { get; set; }

        public int ShiftProductionReportId { get; set; }
        public ShiftProductionReport ShiftProductionReport { get; set; } = null!;

        public ICollection<ProducedLineSsccBarcode> SsccBarcodes { get; set; } = new List<ProducedLineSsccBarcode>();
    }
}