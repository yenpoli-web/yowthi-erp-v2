using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.Sales;
using YowThi.Erp.Infrastructure.Persistence.System;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Sales;

internal sealed class SalesAllocationRevisionConfiguration : IEntityTypeConfiguration<SalesAllocationRevision>
{
    public void Configure(EntityTypeBuilder<SalesAllocationRevision> builder)
    {
        builder.ToTable("sales_allocation_revisions", "sales", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_sales_allocation_revisions_revision_number", "revision_number >= 0");
        });

        builder.HasKey(x => x.Id).HasName("pk_sales_allocation_revisions");
        builder.HasAlternateKey(x => new { x.Id, x.SalesId }).HasName("ak_sales_allocation_revisions_id_sales");

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.SalesId).HasColumnName("sales_id").IsRequired();
        builder.Property(x => x.RevisionNumber).HasColumnName("revision_number").IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasColumnType("text");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.CreatedByAccountId).HasColumnName("created_by_account_id").IsRequired();

        builder.HasOne<Sale>().WithMany().HasForeignKey(x => x.SalesId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_sales_allocation_revisions_sales");
        builder.HasOne<SystemAccountRecord>().WithMany().HasForeignKey(x => x.CreatedByAccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_sales_allocation_revisions_created_by_account");

        builder.HasIndex(x => new { x.SalesId, x.RevisionNumber }).IsUnique().HasDatabaseName("ux_sales_allocation_revisions_sales_revision_number");
        builder.HasIndex(x => x.SalesId).HasDatabaseName("ix_sales_allocation_revisions_sales_id");
        builder.HasIndex(x => x.CreatedByAccountId).HasDatabaseName("ix_sales_allocation_revisions_created_by_account_id");
    }
}
