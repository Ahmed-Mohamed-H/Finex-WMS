using Finex.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finex.Infrastructure.Persistence.Configurations.Production
{
    public sealed class ProducedLineSsccBarcodeConfiguration : IEntityTypeConfiguration<ProducedLineSsccBarcode>
    {
        public void Configure(EntityTypeBuilder<ProducedLineSsccBarcode> builder)
        {
            builder.ToTable("ProducedLineSsccBarcodes");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.SsccBarcode)
                .HasMaxLength(20)
                .IsUnicode(false)
                .IsRequired();

            builder.HasIndex(x => x.SsccBarcode)
                .IsUnique();
        }
    }
}