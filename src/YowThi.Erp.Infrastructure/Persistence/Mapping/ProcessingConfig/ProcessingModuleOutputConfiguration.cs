using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.ProcessingConfiguration;
using YowThi.Erp.Domain.Product;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.ProcessingConfig;

internal sealed class ProcessingModuleOutputConfiguration : IEntityTypeConfiguration<ProcessingModuleOutput>
{
    public void Configure(EntityTypeBuilder<ProcessingModuleOutput> builder)
    {
        builder.ToTable("processing_module_outputs", "processing_config", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_processing_module_outputs_sequence", "output_sequence > 0");
            tableBuilder.HasCheckConstraint("ck_processing_module_outputs_kind", "output_kind IN ('PROCESS_MATERIAL', 'SALES_PRODUCT')");
            tableBuilder.HasCheckConstraint("ck_processing_module_outputs_typed_output", "(output_kind = 'PROCESS_MATERIAL' AND process_material_id IS NOT NULL AND sales_product_id IS NULL) OR (output_kind = 'SALES_PRODUCT' AND process_material_id IS NULL AND sales_product_id IS NOT NULL)");
            tableBuilder.HasCheckConstraint("ck_processing_module_outputs_default_wage_rate", "default_wage_rate NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND default_wage_rate >= 0");
        });

        builder.HasKey(x => x.Id).HasName("pk_processing_module_outputs");
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.ProcessingModuleId).HasColumnName("processing_module_id").IsRequired();
        builder.Property(x => x.ProcessingRouteVersionId).HasColumnName("processing_route_version_id").IsRequired();
        builder.Property(x => x.OutputSequence).HasColumnName("output_sequence").IsRequired();
        builder.Property(x => x.OutputKind).HasColumnName("output_kind").HasConversion<string>().HasColumnType("text").IsRequired();
        builder.Property(x => x.ProcessMaterialId).HasColumnName("process_material_id");
        builder.Property(x => x.SalesProductId).HasColumnName("sales_product_id");
        builder.Property(x => x.DefaultWageRate).HasColumnName("default_wage_rate").HasColumnType("numeric").IsRequired();

        builder.HasOne<ProcessingModule>().WithMany().HasForeignKey(x => new { x.ProcessingModuleId, x.ProcessingRouteVersionId }).HasPrincipalKey(x => new { x.Id, x.ProcessingRouteVersionId }).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_processing_module_outputs_module_route_version");
        builder.HasOne<ProcessMaterial>().WithMany().HasForeignKey(x => new { x.ProcessMaterialId, x.ProcessingRouteVersionId }).HasPrincipalKey(x => new { x.Id, x.ProcessingRouteVersionId }).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_processing_module_outputs_material_route_version");
        builder.HasOne<SalesProduct>().WithMany().HasForeignKey(x => x.SalesProductId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_processing_module_outputs_sales_product");

        builder.HasIndex(x => new { x.ProcessingModuleId, x.OutputSequence }).IsUnique().HasDatabaseName("ux_processing_module_outputs_module_sequence");
        builder.HasIndex(x => new { x.ProcessingModuleId, x.ProcessingRouteVersionId }).HasDatabaseName("ix_processing_module_outputs_module_route_version");
        builder.HasIndex(x => new { x.ProcessMaterialId, x.ProcessingRouteVersionId }).HasDatabaseName("ix_processing_module_outputs_material_route_version");
        builder.HasIndex(x => x.ProcessingRouteVersionId).HasDatabaseName("ix_processing_module_outputs_route_version_id");
        builder.HasIndex(x => x.SalesProductId).HasDatabaseName("ix_processing_module_outputs_sales_product_id");
    }
}
