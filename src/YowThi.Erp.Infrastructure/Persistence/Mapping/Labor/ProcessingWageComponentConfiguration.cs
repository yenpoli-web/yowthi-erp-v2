using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.Labor;
using YowThi.Erp.Domain.ProcessingConfiguration;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Labor;

internal sealed class ProcessingWageComponentConfiguration : IEntityTypeConfiguration<ProcessingWageComponent>
{
    public void Configure(EntityTypeBuilder<ProcessingWageComponent> builder)
    {
        builder.ToTable("processing_wage_components", "labor", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_processing_wage_components_configured_rate", "configured_wage_rate_snapshot NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND configured_wage_rate_snapshot >= 0");
            tableBuilder.HasCheckConstraint("ck_processing_wage_components_applied_rate", "applied_wage_rate NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND applied_wage_rate >= 0");
            tableBuilder.HasCheckConstraint("ck_processing_wage_components_aggregated_quantity", "aggregated_quantity NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND aggregated_quantity >= 0");
            tableBuilder.HasCheckConstraint("ck_processing_wage_components_amount", "amount_thb >= 0");
            tableBuilder.HasCheckConstraint("ck_processing_wage_components_amount_formula", "amount_thb = floor(aggregated_quantity * applied_wage_rate)");
        });

        builder.HasKey(x => x.Id).HasName("pk_processing_wage_components");
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.EmployeeDailyWageId).HasColumnName("employee_daily_wage_id").IsRequired();
        builder.Property(x => x.ProcessingModuleOutputId).HasColumnName("processing_module_output_id").IsRequired();
        builder.Property(x => x.ConfiguredWageRateSnapshot).HasColumnName("configured_wage_rate_snapshot").HasColumnType("numeric").IsRequired();
        builder.Property(x => x.AppliedWageRate).HasColumnName("applied_wage_rate").HasColumnType("numeric").IsRequired();
        builder.Property(x => x.RateOverridden).HasColumnName("rate_overridden").IsRequired();
        builder.Property(x => x.AggregatedQuantity).HasColumnName("aggregated_quantity").HasColumnType("numeric").IsRequired();
        builder.Property(x => x.AmountThb).HasColumnName("amount_thb").IsRequired();

        builder.HasOne<EmployeeDailyWage>().WithMany().HasForeignKey(x => x.EmployeeDailyWageId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_processing_wage_components_daily_wage");
        builder.HasOne<ProcessingModuleOutput>().WithMany().HasForeignKey(x => x.ProcessingModuleOutputId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_processing_wage_components_module_output");

        builder.HasIndex(x => x.EmployeeDailyWageId).HasDatabaseName("ix_processing_wage_components_daily_wage_id");
        builder.HasIndex(x => x.ProcessingModuleOutputId).HasDatabaseName("ix_processing_wage_components_module_output_id");
    }
}
