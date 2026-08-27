using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.Party;
using YowThi.Erp.Domain.Procurement;
using YowThi.Erp.Infrastructure.Persistence.System;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Procurement;

internal sealed class ProcurementEntryConfiguration : IEntityTypeConfiguration<ProcurementEntry>
{
    public void Configure(EntityTypeBuilder<ProcurementEntry> builder)
    {
        builder.ToTable("procurement_entries", "procurement", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_procurement_entries_source_type", "source_type IN ('SUPPLIER', 'FARMER')");
            tableBuilder.HasCheckConstraint("ck_procurement_entries_typed_source", "(source_type = 'SUPPLIER' AND supplier_id IS NOT NULL AND farmer_id IS NULL) OR (source_type = 'FARMER' AND supplier_id IS NULL AND farmer_id IS NOT NULL)");
            tableBuilder.HasCheckConstraint("ck_procurement_entries_net_quantity", "net_quantity NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND net_quantity >= 0");
            tableBuilder.HasCheckConstraint("ck_procurement_entries_unit_code_snapshot", "NULLIF(btrim(unit_code_snapshot), '') IS NOT NULL");
            tableBuilder.HasCheckConstraint("ck_procurement_entries_unit_price", "unit_price NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND unit_price >= 0");
            tableBuilder.HasCheckConstraint("ck_procurement_entries_amount", "amount_thb = floor(net_quantity * unit_price)");
            tableBuilder.HasCheckConstraint("ck_procurement_entries_row_version", "row_version >= 1");
            tableBuilder.HasCheckConstraint("ck_procurement_entries_deleted_pair", "(deleted_at IS NULL AND deleted_by_account_id IS NULL) OR (deleted_at IS NOT NULL AND deleted_by_account_id IS NOT NULL)");
        });

        builder.HasKey(x => x.Id).HasName("pk_procurement_entries");

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.ProcurementBatchId).HasColumnName("procurement_batch_id").IsRequired();
        builder.Property(x => x.SourceType).HasColumnName("source_type").HasConversion<string>().HasColumnType("text").IsRequired();
        builder.Property(x => x.SupplierId).HasColumnName("supplier_id");
        builder.Property(x => x.FarmerId).HasColumnName("farmer_id");
        builder.Property(x => x.NetQuantity).HasColumnName("net_quantity").HasColumnType("numeric").IsRequired();
        builder.Property(x => x.UnitCodeSnapshot).HasColumnName("unit_code_snapshot").HasColumnType("text").IsRequired();
        builder.Property(x => x.UnitPrice).HasColumnName("unit_price").HasColumnType("numeric").IsRequired();
        builder.Property(x => x.AmountThb).HasColumnName("amount_thb").IsRequired();
        builder.Property(x => x.CompanyPickup).HasColumnName("company_pickup").IsRequired();
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.RecordedByAccountId).HasColumnName("recorded_by_account_id").IsRequired();
        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(1L).IsConcurrencyToken();
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
        builder.Property(x => x.DeletedByAccountId).HasColumnName("deleted_by_account_id");

        builder.HasOne<ProcurementBatch>().WithMany().HasForeignKey(x => x.ProcurementBatchId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_procurement_entries_batch");
        builder.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_procurement_entries_supplier");
        builder.HasOne<Farmer>().WithMany().HasForeignKey(x => x.FarmerId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_procurement_entries_farmer");
        builder.HasOne<SystemAccountRecord>().WithMany().HasForeignKey(x => x.RecordedByAccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_procurement_entries_recorded_by_account");
        builder.HasOne<SystemAccountRecord>().WithMany().HasForeignKey(x => x.DeletedByAccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_procurement_entries_deleted_by_account");

        builder.HasIndex(x => x.ProcurementBatchId).HasDatabaseName("ix_procurement_entries_batch_id");
        builder.HasIndex(x => x.SupplierId).HasDatabaseName("ix_procurement_entries_supplier_id");
        builder.HasIndex(x => x.FarmerId).HasDatabaseName("ix_procurement_entries_farmer_id");
        builder.HasIndex(x => x.RecordedByAccountId).HasDatabaseName("ix_procurement_entries_recorded_by_account_id");
        builder.HasIndex(x => x.DeletedByAccountId).HasDatabaseName("ix_procurement_entries_deleted_by_account_id");
    }
}
