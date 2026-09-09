using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YowThi.Erp.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class P8ProcessingDetailLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                schema: "processing",
                table: "processing_execution_inputs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "deleted_by_account_id",
                schema: "processing",
                table: "processing_execution_inputs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "row_version",
                schema: "processing",
                table: "processing_execution_inputs",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                schema: "processing",
                table: "processing_execution_outputs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "deleted_by_account_id",
                schema: "processing",
                table: "processing_execution_outputs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "row_version",
                schema: "processing",
                table: "processing_execution_outputs",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddCheckConstraint(
                name: "ck_processing_execution_inputs_deleted_pair",
                schema: "processing",
                table: "processing_execution_inputs",
                sql: "(deleted_at IS NULL AND deleted_by_account_id IS NULL) OR (deleted_at IS NOT NULL AND deleted_by_account_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_processing_execution_inputs_row_version",
                schema: "processing",
                table: "processing_execution_inputs",
                sql: "row_version >= 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_processing_execution_outputs_deleted_pair",
                schema: "processing",
                table: "processing_execution_outputs",
                sql: "(deleted_at IS NULL AND deleted_by_account_id IS NULL) OR (deleted_at IS NOT NULL AND deleted_by_account_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_processing_execution_outputs_row_version",
                schema: "processing",
                table: "processing_execution_outputs",
                sql: "row_version >= 1");

            migrationBuilder.CreateIndex(
                name: "ix_processing_execution_inputs_deleted_by_account_id",
                schema: "processing",
                table: "processing_execution_inputs",
                column: "deleted_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_processing_execution_outputs_deleted_by_account_id",
                schema: "processing",
                table: "processing_execution_outputs",
                column: "deleted_by_account_id");

            migrationBuilder.AddForeignKey(
                name: "fk_processing_execution_inputs_deleted_by_account",
                schema: "processing",
                table: "processing_execution_inputs",
                column: "deleted_by_account_id",
                principalSchema: "system",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_processing_execution_outputs_deleted_by_account",
                schema: "processing",
                table: "processing_execution_outputs",
                column: "deleted_by_account_id",
                principalSchema: "system",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_processing_execution_inputs_deleted_by_account",
                schema: "processing",
                table: "processing_execution_inputs");

            migrationBuilder.DropForeignKey(
                name: "fk_processing_execution_outputs_deleted_by_account",
                schema: "processing",
                table: "processing_execution_outputs");

            migrationBuilder.DropIndex(
                name: "ix_processing_execution_inputs_deleted_by_account_id",
                schema: "processing",
                table: "processing_execution_inputs");

            migrationBuilder.DropIndex(
                name: "ix_processing_execution_outputs_deleted_by_account_id",
                schema: "processing",
                table: "processing_execution_outputs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_processing_execution_inputs_deleted_pair",
                schema: "processing",
                table: "processing_execution_inputs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_processing_execution_inputs_row_version",
                schema: "processing",
                table: "processing_execution_inputs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_processing_execution_outputs_deleted_pair",
                schema: "processing",
                table: "processing_execution_outputs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_processing_execution_outputs_row_version",
                schema: "processing",
                table: "processing_execution_outputs");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                schema: "processing",
                table: "processing_execution_inputs");

            migrationBuilder.DropColumn(
                name: "deleted_by_account_id",
                schema: "processing",
                table: "processing_execution_inputs");

            migrationBuilder.DropColumn(
                name: "row_version",
                schema: "processing",
                table: "processing_execution_inputs");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                schema: "processing",
                table: "processing_execution_outputs");

            migrationBuilder.DropColumn(
                name: "deleted_by_account_id",
                schema: "processing",
                table: "processing_execution_outputs");

            migrationBuilder.DropColumn(
                name: "row_version",
                schema: "processing",
                table: "processing_execution_outputs");
        }
    }
}
