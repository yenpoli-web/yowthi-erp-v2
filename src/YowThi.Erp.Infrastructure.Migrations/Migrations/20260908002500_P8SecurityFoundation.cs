using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YowThi.Erp.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class P8SecurityFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_capability_grants",
                schema: "system",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    capability_name = table.Column<string>(type: "text", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_capability_grants", x => x.id);
                    table.CheckConstraint("ck_account_capability_grants_capability_nonblank", "btrim(capability_name) <> ''");
                    table.CheckConstraint("ck_account_capability_grants_row_version", "row_version >= 1");
                    table.ForeignKey(
                        name: "fk_account_capability_grants_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_account_capability_grants_accounts_created_by_account_id",
                        column: x => x.created_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_capability_grants_created_by_account_id",
                schema: "system",
                table: "account_capability_grants",
                column: "created_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ux_account_capability_grants_account_capability",
                schema: "system",
                table: "account_capability_grants",
                columns: new[] { "account_id", "capability_name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_capability_grants",
                schema: "system");
        }
    }
}
