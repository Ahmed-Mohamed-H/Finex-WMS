using Finex.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;


namespace Finex.Infrastructure.Persistence.Configurations.Production
{
    public sealed class ProducedLineConfiguration : IEntityTypeConfiguration<ProducedLine>
    {
        public void Configure(EntityTypeBuilder<ProducedLine> builder)
        {
            builder.ToTable("ProducedLines");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Sku)
                .HasMaxLength(50)
                .IsUnicode(false)
                .IsRequired();

            builder.Property(x => x.UnitName)
                .HasMaxLength(150) 
                .IsRequired();

            builder.Property(x => x.UnitBarcode)
                .HasMaxLength(50)
                .IsUnicode(false)
                .IsRequired();

            builder.HasMany(x => x.SsccBarcodes)
                .WithOne(b => b.ProducedLine)
                .HasForeignKey(b => b.ProducedLineId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}