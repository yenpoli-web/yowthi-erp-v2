using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YowThi.Erp.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class P8ProcurementReceiptDestination : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "receipt_storage_location_id",
                schema: "procurement",
                table: "procurement_batches",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                WITH unique_receipt_locations AS (
                    SELECT
                        procurement_batch_id,
                        (array_agg(DISTINCT storage_location_id))[1] AS storage_location_id
                    FROM inventory.inventory_movements
                    WHERE movement_type = 'PURCHASE_RECEIPT'
                      AND procurement_batch_id IS NOT NULL
                    GROUP BY procurement_batch_id
                    HAVING count(DISTINCT storage_location_id) = 1
                )
                UPDATE procurement.procurement_batches AS batch
                SET receipt_storage_location_id = source.storage_location_id
                FROM unique_receipt_locations AS source
                WHERE batch.id = source.procurement_batch_id
                  AND batch.receipt_storage_location_id IS NULL;
                """);

            migrationBuilder.Sql(
                """
                UPDATE procurement.procurement_batches AS batch
                SET receipt_storage_location_id = product.default_storage_location_id
                FROM product.procurement_products AS product
                WHERE batch.procurement_product_id = product.id
                  AND batch.receipt_storage_location_id IS NULL
                  AND product.default_storage_location_id IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1
                      FROM inventory.inventory_movements AS movement
                      WHERE movement.procurement_batch_id = batch.id
                        AND movement.movement_type = 'PURCHASE_RECEIPT'
                  );
                """);

            migrationBuilder.CreateIndex(
                name: "ix_procurement_batches_receipt_storage_location_id",
                schema: "procurement",
                table: "procurement_batches",
                column: "receipt_storage_location_id");

            migrationBuilder.AddForeignKey(
                name: "fk_procurement_batches_receipt_storage_location",
                schema: "procurement",
                table: "procurement_batches",
                column: "receipt_storage_location_id",
                principalSchema: "infrastructure",
                principalTable: "storage_locations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_procurement_batches_receipt_storage_location",
                schema: "procurement",
                table: "procurement_batches");

            migrationBuilder.DropIndex(
                name: "ix_procurement_batches_receipt_storage_location_id",
                schema: "procurement",
                table: "procurement_batches");

            migrationBuilder.DropColumn(
                name: "receipt_storage_location_id",
                schema: "procurement",
                table: "procurement_batches");
        }
    }
}
