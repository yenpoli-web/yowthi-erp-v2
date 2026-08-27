using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.ProcessingConfiguration;
using YowThi.Erp.Domain.Product;
using YowThi.Erp.Infrastructure.Persistence.System;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.ProcessingConfig;

internal sealed class ProcessingRouteConfiguration : IEntityTypeConfiguration<ProcessingRoute>
{
    public void Configure(EntityTypeBuilder<ProcessingRoute> builder)
    {
        builder.ToTable("processing_routes", "processing_config", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_processing_routes_name_present", "NULLIF(btrim(name_zh_tw), '') IS NOT NULL OR NULLIF(btrim(name_th_th), '') IS NOT NULL");
            tableBuilder.HasCheckConstraint("ck_processing_routes_row_version", "row_version >= 1");
            tableBuilder.HasCheckConstraint("ck_processing_routes_deleted_pair", "(deleted_at IS NULL AND deleted_by_account_id IS NULL) OR (deleted_at IS NOT NULL AND deleted_by_account_id IS NOT NULL)");
        });

        builder.HasKey(x => x.Id).HasName("pk_processing_routes");
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.ProcurementProductId).HasColumnName("procurement_product_id").IsRequired();
        builder.Property(x => x.NameZhTw).HasColumnName("name_zh_tw").HasColumnType("text");
        builder.Property(x => x.NameThTh).HasColumnName("name_th_th").HasColumnType("text");
        builder.Property(x => x.Active).HasColumnName("active").IsRequired();
        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(1L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.CreatedByAccountId).HasColumnName("created_by_account_id").IsRequired();
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
        builder.Property(x => x.DeletedByAccountId).HasColumnName("deleted_by_account_id");

        builder.HasOne<ProcurementProduct>().WithMany().HasForeignKey(x => x.ProcurementProductId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_processing_routes_procurement_product");
        builder.HasOne<SystemAccountRecord>().WithMany().HasForeignKey(x => x.CreatedByAccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_processing_routes_created_by_account");
        builder.HasOne<SystemAccountRecord>().WithMany().HasForeignKey(x => x.DeletedByAccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_processing_routes_deleted_by_account");
        builder.HasIndex(x => x.ProcurementProductId).HasDatabaseName("ix_processing_routes_procurement_product_id");
        builder.HasIndex(x => x.CreatedByAccountId).HasDatabaseName("ix_processing_routes_created_by_account_id");
        builder.HasIndex(x => x.DeletedByAccountId).HasDatabaseName("ix_processing_routes_deleted_by_account_id");
    }
}
