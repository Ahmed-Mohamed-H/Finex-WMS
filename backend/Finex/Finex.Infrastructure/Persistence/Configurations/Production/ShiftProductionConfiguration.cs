using Finex.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finex.Infrastructure.Persistence.Configurations.Production;

public sealed class ShiftProductionReportConfiguration : IEntityTypeConfiguration<ShiftProductionReport>
{
    public void Configure(EntityTypeBuilder<ShiftProductionReport> builder)
    {
        builder.ToTable("ShiftProductionReports");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.PlantCode)
            .HasMaxLength(50)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.LineCode)
            .HasMaxLength(50)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.ShiftCode)
            .HasMaxLength(50)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.ShiftSupervisorCode)
            .HasMaxLength(50)
            .IsUnicode(false)
            .IsRequired();

        builder.HasIndex(x => new { x.PlantCode, x.LineCode, x.ProductionDate, x.ShiftCode })
            .IsUnique();

        builder.HasMany(x => x.ProducedLines)
            .WithOne(p => p.ShiftProductionReport)
            .HasForeignKey(p => p.ShiftProductionReportId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}