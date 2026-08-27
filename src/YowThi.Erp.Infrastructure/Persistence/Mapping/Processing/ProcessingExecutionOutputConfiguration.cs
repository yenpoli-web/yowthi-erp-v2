using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.Processing;
using YowThi.Erp.Domain.ProcessingConfiguration;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Processing;

internal sealed class ProcessingExecutionOutputConfiguration : IEntityTypeConfiguration<ProcessingExecutionOutput>
{
    public void Configure(EntityTypeBuilder<ProcessingExecutionOutput> builder)
    {
        builder.ToTable("processing_execution_outputs", "processing", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_processing_execution_outputs_kind", "output_kind_snapshot IN ('PROCESS_MATERIAL', 'SALES_PRODUCT')");
            tableBuilder.HasCheckConstraint("ck_processing_execution_outputs_wage_rate", "configured_wage_rate_snapshot NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND configured_wage_rate_snapshot >= 0");
            tableBuilder.HasCheckConstraint("ck_processing_execution_outputs_scale_reading", "observed_scale_reading IS NULL OR (observed_scale_reading NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND observed_scale_reading >= 0 AND observed_scale_reading = round(observed_scale_reading, 1))");
            tableBuilder.HasCheckConstraint("ck_processing_execution_outputs_container_count", "actual_container_count IS NULL OR actual_container_count >= 0");
            tableBuilder.HasCheckConstraint("ck_processing_execution_outputs_tare_snapshot", "tare_weight_snapshot IS NULL OR (tare_weight_snapshot NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND tare_weight_snapshot >= 0)");
            tableBuilder.HasCheckConstraint("ck_processing_execution_outputs_derived_net_quantity", "derived_net_quantity IS NULL OR (derived_net_quantity NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND derived_net_quantity >= 0 AND derived_net_quantity = round(derived_net_quantity, 1))");
            tableBuilder.HasCheckConstraint("ck_processing_execution_outputs_completed_quantity", "completed_quantity IS NULL OR (completed_quantity NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND completed_quantity >= 0 AND completed_quantity = round(completed_quantity, 1))");
            tableBuilder.HasCheckConstraint("ck_processing_execution_outputs_packaging_weight", "packaging_weight_snapshot IS NULL OR (packaging_weight_snapshot NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND packaging_weight_snapshot > 0)");
            tableBuilder.HasCheckConstraint("ck_processing_execution_outputs_source_consumption", "source_consumption_quantity IS NULL OR (source_consumption_quantity NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND source_consumption_quantity >= 0)");
            tableBuilder.HasCheckConstraint("ck_processing_execution_outputs_shape", "(output_kind_snapshot = 'PROCESS_MATERIAL' AND observed_scale_reading IS NOT NULL AND derived_net_quantity IS NOT NULL AND completed_quantity IS NULL AND packaging_weight_snapshot IS NULL AND source_consumption_quantity IS NULL AND (((actual_container_count IS NULL AND tare_weight_snapshot IS NULL) AND derived_net_quantity = observed_scale_reading) OR (actual_container_count IS NOT NULL AND tare_weight_snapshot IS NOT NULL AND derived_net_quantity = observed_scale_reading - (actual_container_count * tare_weight_snapshot)))) OR (output_kind_snapshot = 'SALES_PRODUCT' AND observed_scale_reading IS NULL AND actual_container_count IS NULL AND tare_weight_snapshot IS NULL AND derived_net_quantity IS NULL AND completed_quantity IS NOT NULL AND packaging_weight_snapshot IS NOT NULL AND source_consumption_quantity IS NOT NULL)");
            tableBuilder.HasCheckConstraint("ck_processing_execution_outputs_final_packaging_formula", "output_kind_snapshot <> 'SALES_PRODUCT' OR source_consumption_quantity = completed_quantity * packaging_weight_snapshot");
        });

        builder.HasKey(x => x.Id).HasName("pk_processing_execution_outputs");
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.ProcessingExecutionId).HasColumnName("processing_execution_id").IsRequired();
        builder.Property(x => x.ProcessingModuleOutputId).HasColumnName("processing_module_output_id").IsRequired();
        builder.Property(x => x.OutputKindSnapshot).HasColumnName("output_kind_snapshot").HasConversion<string>().HasColumnType("text").IsRequired();
        builder.Property(x => x.ConfiguredWageRateSnapshot).HasColumnName("configured_wage_rate_snapshot").HasColumnType("numeric").IsRequired();
        builder.Property(x => x.ObservedScaleReading).HasColumnName("observed_scale_reading").HasColumnType("numeric");
        builder.Property(x => x.ActualContainerCount).HasColumnName("actual_container_count");
        builder.Property(x => x.TareWeightSnapshot).HasColumnName("tare_weight_snapshot").HasColumnType("numeric");
        builder.Property(x => x.DerivedNetQuantity).HasColumnName("derived_net_quantity").HasColumnType("numeric");
        builder.Property(x => x.CompletedQuantity).HasColumnName("completed_quantity").HasColumnType("numeric");
        builder.Property(x => x.PackagingWeightSnapshot).HasColumnName("packaging_weight_snapshot").HasColumnType("numeric");
        builder.Property(x => x.SourceConsumptionQuantity).HasColumnName("source_consumption_quantity").HasColumnType("numeric");

        builder.HasOne<ProcessingExecution>().WithMany().HasForeignKey(x => x.ProcessingExecutionId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_processing_execution_outputs_execution");
        builder.HasOne<ProcessingModuleOutput>().WithMany().HasForeignKey(x => x.ProcessingModuleOutputId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_processing_execution_outputs_module_output");

        builder.HasIndex(x => new { x.ProcessingExecutionId, x.ProcessingModuleOutputId }).IsUnique().HasDatabaseName("ux_processing_execution_outputs_execution_definition");
        builder.HasIndex(x => x.ProcessingExecutionId).IsUnique().HasFilter("output_kind_snapshot = 'SALES_PRODUCT'").HasDatabaseName("ux_processing_execution_outputs_one_sales_product_per_execution");
        builder.HasIndex(x => x.ProcessingModuleOutputId).HasDatabaseName("ix_processing_execution_outputs_module_output_id");
    }
}
