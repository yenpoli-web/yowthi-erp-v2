using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YowThi.Erp.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class P8PartyBusinessCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "code",
                schema: "party",
                table: "suppliers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "code",
                schema: "party",
                table: "outsourced_vendors",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "code",
                schema: "party",
                table: "farmers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "code",
                schema: "party",
                table: "employees",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "code",
                schema: "party",
                table: "customers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "code",
                schema: "party",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "code",
                schema: "party",
                table: "outsourced_vendors");

            migrationBuilder.DropColumn(
                name: "code",
                schema: "party",
                table: "farmers");

            migrationBuilder.DropColumn(
                name: "code",
                schema: "party",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "code",
                schema: "party",
                table: "customers");
        }
    }
}
