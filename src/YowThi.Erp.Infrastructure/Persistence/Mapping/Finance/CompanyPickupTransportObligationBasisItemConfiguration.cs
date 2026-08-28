using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.Finance;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Finance;

internal sealed class CompanyPickupTransportObligationBasisItemConfiguration : IEntityTypeConfiguration<CompanyPickupTransportObligationBasisItem>
{
    public void Configure(EntityTypeBuilder<CompanyPickupTransportObligationBasisItem> builder)
    {
        builder.ToTable("company_pickup_transport_obligation_basis_items", "finance", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_company_pickup_transport_obligation_basis_items_quantity", "applied_quantity NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND applied_quantity >= 0");
        });

        builder.HasKey(x => new { x.TransportObligationItemId, x.TransportBasisId }).HasName("pk_company_pickup_transport_obligation_basis_items");
        builder.Property(x => x.TransportObligationItemId).HasColumnName("transport_obligation_item_id").IsRequired();
        builder.Property(x => x.TransportBasisId).HasColumnName("transport_basis_id").IsRequired();
        builder.Property(x => x.AppliedQuantity).HasColumnName("applied_quantity").HasColumnType("numeric").IsRequired();

        builder.HasOne<PayableObligationItem>().WithMany().HasForeignKey(x => x.TransportObligationItemId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_company_pickup_transport_obligation_basis_items_obligation");
        builder.HasOne<CompanyPickupTransportBasis>().WithMany().HasForeignKey(x => x.TransportBasisId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_company_pickup_transport_obligation_basis_items_basis");
        builder.HasIndex(x => x.TransportBasisId).HasDatabaseName("ix_company_pickup_transport_obligation_basis_items_basis_id");
    }
}
