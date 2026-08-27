using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.ProcessingConfiguration;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.ProcessingConfig;

internal sealed class ProcessingModuleConfiguration : IEntityTypeConfiguration<ProcessingModule>
{
    public void Configure(EntityTypeBuilder<ProcessingModule> builder)
    {
        builder.ToTable("processing_modules", "processing_config", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_processing_modules_name_present", "NULLIF(btrim(name_zh_tw), '') IS NOT NULL OR NULLIF(btrim(name_th_th), '') IS NOT NULL");
            tableBuilder.HasCheckConstraint("ck_processing_modules_execution_mode", "execution_mode IN ('SOURCE_TRACKED', 'POOLED_OUTPUT', 'FINAL_PACKAGING')");
            tableBuilder.HasCheckConstraint("ck_processing_modules_negative_inventory_policy", "NULLIF(btrim(negative_inventory_policy), '') IS NOT NULL");
        });

        builder.HasKey(x => x.Id).HasName("pk_processing_modules");
        builder.HasAlternateKey(x => new { x.Id, x.ProcessingRouteVersionId }).HasName("ak_processing_modules_id_route_version");

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.ProcessingRouteVersionId).HasColumnName("processing_route_version_id").IsRequired();
        builder.Property(x => x.NameZhTw).HasColumnName("name_zh_tw").HasColumnType("text");
        builder.Property(x => x.NameThTh).HasColumnName("name_th_th").HasColumnType("text");
        builder.Property(x => x.ExecutionMode).HasColumnName("execution_mode").HasConversion<string>().HasColumnType("text").IsRequired();
        builder.Property(x => x.InputProcessMaterialId).HasColumnName("input_process_material_id");
        builder.Property(x => x.NegativeInventoryPolicy).HasColumnName("negative_inventory_policy").HasColumnType("text").IsRequired();

        builder.HasOne<ProcessingRouteVersion>().WithMany().HasForeignKey(x => x.ProcessingRouteVersionId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_processing_modules_route_version");
        builder.HasOne<ProcessMaterial>().WithMany().HasForeignKey(x => new { x.InputProcessMaterialId, x.ProcessingRouteVersionId }).HasPrincipalKey(x => new { x.Id, x.ProcessingRouteVersionId }).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_processing_modules_input_material_route_version");
        builder.HasIndex(x => x.ProcessingRouteVersionId).HasDatabaseName("ix_processing_modules_route_version_id");
        builder.HasIndex(x => new { x.InputProcessMaterialId, x.ProcessingRouteVersionId }).HasDatabaseName("ix_processing_modules_input_material_route_version");
    }
}
