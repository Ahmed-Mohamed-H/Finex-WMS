using System;
using System.Collections.Generic;
using System.Text;

namespace Finex.Domain.Entities
{
    public class ShiftProductionReport
    {
        public int Id { get; set; }
        public string PlantCode { get; set; } = string.Empty;
        public string LineCode { get; set; } = string.Empty;
        public string ShiftCode { get; set; } = string.Empty;
        public DateOnly ProductionDate { get; set; }
        public DateTimeOffset ShiftStartedAt { get; set; }
        public DateTimeOffset ShiftEndedAt { get; set; }
        public string ShiftSupervisorCode { get; set; } = string.Empty;

        public ICollection<ProducedLine> ProducedLines { get; set; } = new List<ProducedLine>();
    }
}
