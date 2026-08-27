using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.Processing;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Processing;

internal sealed class ProcessingExecutionInputConfiguration : IEntityTypeConfiguration<ProcessingExecutionInput>
{
    public void Configure(EntityTypeBuilder<ProcessingExecutionInput> builder)
    {
        builder.ToTable("processing_execution_inputs", "processing", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_processing_execution_inputs_consumption_basis", "consumption_basis IN ('SCALE_NET', 'OUTPUT_QUANTITY', 'PACKAGING_WEIGHT')");
            tableBuilder.HasCheckConstraint("ck_processing_execution_inputs_consumed_quantity", "consumed_quantity NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND consumed_quantity >= 0 AND consumed_quantity = round(consumed_quantity, 1)");
            tableBuilder.HasCheckConstraint("ck_processing_execution_inputs_scale_reading", "observed_scale_reading IS NULL OR (observed_scale_reading NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND observed_scale_reading >= 0 AND observed_scale_reading = round(observed_scale_reading, 1))");
            tableBuilder.HasCheckConstraint("ck_processing_execution_inputs_container_count", "actual_container_count IS NULL OR actual_container_count >= 0");
            tableBuilder.HasCheckConstraint("ck_processing_execution_inputs_tare_snapshot", "tare_weight_snapshot IS NULL OR (tare_weight_snapshot NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND tare_weight_snapshot >= 0)");
            tableBuilder.HasCheckConstraint("ck_processing_execution_inputs_derived_net_quantity", "derived_net_quantity IS NULL OR (derived_net_quantity NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND derived_net_quantity >= 0 AND derived_net_quantity = round(derived_net_quantity, 1))");
            tableBuilder.HasCheckConstraint("ck_processing_execution_inputs_shape", "(consumption_basis = 'SCALE_NET' AND observed_scale_reading IS NOT NULL AND derived_net_quantity IS NOT NULL AND consumed_quantity = derived_net_quantity AND (((actual_container_count IS NULL AND tare_weight_snapshot IS NULL) AND derived_net_quantity = observed_scale_reading) OR (actual_container_count IS NOT NULL AND tare_weight_snapshot IS NOT NULL AND derived_net_quantity = observed_scale_reading - (actual_container_count * tare_weight_snapshot)))) OR (consumption_basis IN ('OUTPUT_QUANTITY', 'PACKAGING_WEIGHT') AND observed_scale_reading IS NULL AND actual_container_count IS NULL AND tare_weight_snapshot IS NULL AND derived_net_quantity IS NULL)");
        });

        builder.HasKey(x => x.ProcessingExecutionId).HasName("pk_processing_execution_inputs");
        builder.Property(x => x.ProcessingExecutionId).HasColumnName("processing_execution_id").ValueGeneratedNever();
        builder.Property(x => x.ConsumptionBasis).HasColumnName("consumption_basis").HasConversion<string>().HasColumnType("text").IsRequired();
        builder.Property(x => x.ConsumedQuantity).HasColumnName("consumed_quantity").HasColumnType("numeric").IsRequired();
        builder.Property(x => x.ObservedScaleReading).HasColumnName("observed_scale_reading").HasColumnType("numeric");
        builder.Property(x => x.ActualContainerCount).HasColumnName("actual_container_count");
        builder.Property(x => x.TareWeightSnapshot).HasColumnName("tare_weight_snapshot").HasColumnType("numeric");
        builder.Property(x => x.DerivedNetQuantity).HasColumnName("derived_net_quantity").HasColumnType("numeric");

        builder.HasOne<ProcessingExecution>().WithOne().HasForeignKey<ProcessingExecutionInput>(x => x.ProcessingExecutionId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_processing_execution_inputs_execution");
    }
}
