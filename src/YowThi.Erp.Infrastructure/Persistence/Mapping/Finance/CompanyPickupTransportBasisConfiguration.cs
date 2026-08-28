using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.Finance;
using YowThi.Erp.Domain.Procurement;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Finance;

internal sealed class CompanyPickupTransportBasisConfiguration : IEntityTypeConfiguration<CompanyPickupTransportBasis>
{
    public void Configure(EntityTypeBuilder<CompanyPickupTransportBasis> builder)
    {
        builder.ToTable("company_pickup_transport_bases", "finance", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_company_pickup_transport_bases_quantity", "applicable_quantity NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND applicable_quantity >= 0");
        });

        builder.HasKey(x => x.Id).HasName("pk_company_pickup_transport_bases");
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.ProcurementEntryId).HasColumnName("procurement_entry_id").IsRequired();
        builder.Property(x => x.ApplicableQuantity).HasColumnName("applicable_quantity").HasColumnType("numeric").IsRequired();
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").HasColumnType("timestamp with time zone").IsRequired();

        builder.HasOne<ProcurementEntry>().WithMany().HasForeignKey(x => x.ProcurementEntryId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_company_pickup_transport_bases_procurement_entry");
        builder.HasIndex(x => x.ProcurementEntryId).IsUnique().HasDatabaseName("ux_company_pickup_transport_bases_procurement_entry");
    }
}
