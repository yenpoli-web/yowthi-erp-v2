using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.Labor;
using YowThi.Erp.Domain.Processing;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Labor;

internal sealed class ProcessingWageComponentSourceConfiguration : IEntityTypeConfiguration<ProcessingWageComponentSource>
{
    public void Configure(EntityTypeBuilder<ProcessingWageComponentSource> builder)
    {
        builder.ToTable("processing_wage_component_sources", "labor");

        builder.HasKey(x => new { x.ProcessingWageComponentId, x.ProcessingExecutionOutputId }).HasName("pk_processing_wage_component_sources");
        builder.Property(x => x.ProcessingWageComponentId).HasColumnName("processing_wage_component_id");
        builder.Property(x => x.ProcessingExecutionOutputId).HasColumnName("processing_execution_output_id");
        builder.Property(x => x.QuantitySnapshot).HasColumnName("quantity_snapshot").HasColumnType("numeric").IsRequired();

        builder.HasOne<ProcessingWageComponent>().WithMany().HasForeignKey(x => x.ProcessingWageComponentId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_processing_wage_component_sources_component");
        builder.HasOne<ProcessingExecutionOutput>().WithMany().HasForeignKey(x => x.ProcessingExecutionOutputId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_processing_wage_component_sources_execution_output");

        builder.HasIndex(x => x.ProcessingExecutionOutputId).IsUnique().HasDatabaseName("ux_processing_wage_component_sources_execution_output");
    }
}
