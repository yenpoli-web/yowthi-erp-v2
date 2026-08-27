using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.Party;
using YowThi.Erp.Domain.Processing;
using YowThi.Erp.Domain.ProcessingConfiguration;
using YowThi.Erp.Domain.Procurement;
using YowThi.Erp.Infrastructure.Persistence.System;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Processing;

internal sealed class ProcessingExecutionConfiguration : IEntityTypeConfiguration<ProcessingExecution>
{
    public void Configure(EntityTypeBuilder<ProcessingExecution> builder)
    {
        builder.ToTable("processing_executions", "processing", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_processing_executions_execution_mode", "execution_mode_snapshot IN ('SOURCE_TRACKED', 'POOLED_OUTPUT', 'FINAL_PACKAGING')");
            tableBuilder.HasCheckConstraint("ck_processing_executions_negative_inventory_policy", "NULLIF(btrim(negative_inventory_policy_snapshot), '') IS NOT NULL");
            tableBuilder.HasCheckConstraint("ck_processing_executions_source_shape", "(execution_mode_snapshot = 'SOURCE_TRACKED' AND ((processing_source_kind = 'SUPPLIER' AND supplier_id IS NOT NULL) OR (processing_source_kind = 'FARMERS_COMBINED' AND supplier_id IS NULL))) OR (execution_mode_snapshot IN ('POOLED_OUTPUT', 'FINAL_PACKAGING') AND processing_source_kind IS NULL AND supplier_id IS NULL)");
            tableBuilder.HasCheckConstraint("ck_processing_executions_row_version", "row_version >= 1");
            tableBuilder.HasCheckConstraint("ck_processing_executions_deleted_pair", "(deleted_at IS NULL AND deleted_by_account_id IS NULL) OR (deleted_at IS NOT NULL AND deleted_by_account_id IS NOT NULL)");
        });

        builder.HasKey(x => x.Id).HasName("pk_processing_executions");

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.WorkDate).HasColumnName("work_date").HasColumnType("date").IsRequired();
        builder.Property(x => x.EmployeeId).HasColumnName("employee_id").IsRequired();
        builder.Property(x => x.ProcurementBatchId).HasColumnName("procurement_batch_id").IsRequired();
        builder.Property(x => x.ProcessingRouteVersionId).HasColumnName("processing_route_version_id").IsRequired();
        builder.Property(x => x.ProcessingModuleId).HasColumnName("processing_module_id").IsRequired();
        builder.Property(x => x.ExecutionModeSnapshot).HasColumnName("execution_mode_snapshot").HasConversion<string>().HasColumnType("text").IsRequired();
        builder.Property(x => x.NegativeInventoryPolicySnapshot).HasColumnName("negative_inventory_policy_snapshot").HasColumnType("text").IsRequired();
        builder.Property(x => x.ProcessingSourceKind).HasColumnName("processing_source_kind").HasConversion<string>().HasColumnType("text");
        builder.Property(x => x.SupplierId).HasColumnName("supplier_id");
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.RecordedByAccountId).HasColumnName("recorded_by_account_id").IsRequired();
        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(1L).IsConcurrencyToken();
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
        builder.Property(x => x.DeletedByAccountId).HasColumnName("deleted_by_account_id");

        builder.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_processing_executions_employee");
        builder.HasOne<ProcurementBatch>().WithMany().HasForeignKey(x => x.ProcurementBatchId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_processing_executions_batch");
        builder.HasOne<ProcessingRouteVersion>().WithMany().HasForeignKey(x => x.ProcessingRouteVersionId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_processing_executions_route_version");
        builder.HasOne<ProcessingModule>().WithMany().HasForeignKey(x => new { x.ProcessingModuleId, x.ProcessingRouteVersionId }).HasPrincipalKey(x => new { x.Id, x.ProcessingRouteVersionId }).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_processing_executions_module_route_version");
        builder.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_processing_executions_supplier");
        builder.HasOne<SystemAccountRecord>().WithMany().HasForeignKey(x => x.RecordedByAccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_processing_executions_recorded_by_account");
        builder.HasOne<SystemAccountRecord>().WithMany().HasForeignKey(x => x.DeletedByAccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_processing_executions_deleted_by_account");

        builder.HasIndex(x => x.EmployeeId).HasDatabaseName("ix_processing_executions_employee_id");
        builder.HasIndex(x => x.ProcurementBatchId).HasDatabaseName("ix_processing_executions_batch_id");
        builder.HasIndex(x => x.ProcessingRouteVersionId).HasDatabaseName("ix_processing_executions_route_version_id");
        builder.HasIndex(x => new { x.ProcessingModuleId, x.ProcessingRouteVersionId }).HasDatabaseName("ix_processing_executions_module_route_version");
        builder.HasIndex(x => x.SupplierId).HasDatabaseName("ix_processing_executions_supplier_id");
        builder.HasIndex(x => x.RecordedByAccountId).HasDatabaseName("ix_processing_executions_recorded_by_account_id");
        builder.HasIndex(x => x.DeletedByAccountId).HasDatabaseName("ix_processing_executions_deleted_by_account_id");
    }
}
