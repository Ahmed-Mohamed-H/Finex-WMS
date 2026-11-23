namespace Finex.Domain.Entities;

public sealed class ProducedLineSsccBarcode
{
    public int Id { get; set; }
    public string SsccBarcode { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public int ProducedLineId { get; set; }
    public ProducedLine ProducedLine { get; set; } = null!;
}