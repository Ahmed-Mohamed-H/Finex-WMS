using Finex.Domain.Entities;
using Finex.Domain.Enums;
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

        builder.Property(x => x.Status)
            .HasDefaultValue(ShiftReportStatus.PendingReview)
            .IsRequired();

        builder.Property(x => x.RevisionNumber)
            .HasDefaultValue(1)
            .IsRequired();

        builder.Property(x => x.RejectionReason)
            .HasMaxLength(500);

        builder.Property(x => x.ReviewedBy)
            .HasMaxLength(100);

        builder.HasIndex(x => new { x.PlantCode, x.LineCode, x.ProductionDate, x.ShiftCode })
            .IsUnique()
            .HasFilter("[Status] <> 3")
            .HasDatabaseName("UX_ShiftProductionReports_ActiveShift");

        builder.HasOne(x => x.SupersededByReport)
            .WithMany()
            .HasForeignKey(x => x.SupersededByReportId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasMany(x => x.ProducedLines)
            .WithOne(p => p.ShiftProductionReport)
            .HasForeignKey(p => p.ShiftProductionReportId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}