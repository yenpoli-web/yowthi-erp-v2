using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YowThi.Erp.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class InitialV01 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "system");

            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.EnsureSchema(
                name: "finance");

            migrationBuilder.EnsureSchema(
                name: "infrastructure");

            migrationBuilder.EnsureSchema(
                name: "party");

            migrationBuilder.EnsureSchema(
                name: "labor");

            migrationBuilder.EnsureSchema(
                name: "inventory");

            migrationBuilder.EnsureSchema(
                name: "outsourced");

            migrationBuilder.EnsureSchema(
                name: "processing_config");

            migrationBuilder.EnsureSchema(
                name: "processing");

            migrationBuilder.EnsureSchema(
                name: "procurement");

            migrationBuilder.EnsureSchema(
                name: "product");

            migrationBuilder.EnsureSchema(
                name: "sales");

            migrationBuilder.EnsureSchema(
                name: "sales_handling");

            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "system",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    identity_issuer = table.Column<string>(type: "text", nullable: true),
                    identity_subject = table.Column<string>(type: "text", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                    table.CheckConstraint("ck_accounts_identity_issuer_nonblank", "identity_issuer IS NULL OR btrim(identity_issuer) <> ''");
                    table.CheckConstraint("ck_accounts_identity_pair", "(identity_issuer IS NULL AND identity_subject IS NULL) OR (identity_issuer IS NOT NULL AND identity_subject IS NOT NULL)");
                    table.CheckConstraint("ck_accounts_identity_subject_nonblank", "identity_subject IS NULL OR btrim(identity_subject) <> ''");
                    table.CheckConstraint("ck_accounts_row_version", "row_version >= 1");
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "system",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_type = table.Column<string>(type: "text", nullable: false),
                    message_version = table.Column<int>(type: "integer", nullable: false),
                    payload = table.Column<JsonElement>(type: "jsonb", nullable: false),
                    command_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    available_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivery_attempt_count = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lock_token = table.Column<Guid>(type: "uuid", nullable: true),
                    last_error_summary = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                    table.CheckConstraint("ck_outbox_messages_delivery_attempt_count", "delivery_attempt_count >= 0");
                    table.CheckConstraint("ck_outbox_messages_message_version", "message_version > 0");
                });

            migrationBuilder.CreateTable(
                name: "audit_events",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    command_id = table.Column<Guid>(type: "uuid", nullable: true),
                    command_type = table.Column<string>(type: "text", nullable: true),
                    event_kind = table.Column<string>(type: "text", nullable: false),
                    actor_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason_text = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_events", x => x.id);
                    table.CheckConstraint("ck_audit_events_event_kind", "event_kind IN ('BUSINESS_COMMAND', 'CORRECTION', 'DATA_LIFECYCLE', 'HARD_DELETE')");
                    table.ForeignKey(
                        name: "fk_audit_events_actor_account",
                        column: x => x.actor_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "command_executions",
                schema: "system",
                columns: table => new
                {
                    command_id = table.Column<Guid>(type: "uuid", nullable: false),
                    command_type = table.Column<string>(type: "text", nullable: false),
                    request_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    result_payload = table.Column<JsonElement>(type: "jsonb", nullable: true),
                    actor_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    executed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_command_executions", x => x.command_id);
                    table.CheckConstraint("ck_command_executions_execution_state", "(status = 'IN_PROGRESS' AND executed_at IS NULL) OR (status = 'SUCCEEDED' AND executed_at IS NOT NULL)");
                    table.CheckConstraint("ck_command_executions_request_hash_length", "octet_length(request_hash) = 32");
                    table.CheckConstraint("ck_command_executions_status", "status IN ('IN_PROGRESS', 'SUCCEEDED')");
                    table.ForeignKey(
                        name: "fk_command_executions_actor_account",
                        column: x => x.actor_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "containers",
                schema: "infrastructure",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_zh_tw = table.Column<string>(type: "text", nullable: true),
                    name_th_th = table.Column<string>(type: "text", nullable: true),
                    tare_weight = table.Column<decimal>(type: "numeric", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_account_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_containers", x => x.id);
                    table.CheckConstraint("ck_containers_deleted_pair", "(deleted_at IS NULL AND deleted_by_account_id IS NULL) OR (deleted_at IS NOT NULL AND deleted_by_account_id IS NOT NULL)");
                    table.CheckConstraint("ck_containers_name_present", "NULLIF(btrim(name_zh_tw), '') IS NOT NULL OR NULLIF(btrim(name_th_th), '') IS NOT NULL");
                    table.CheckConstraint("ck_containers_row_version", "row_version >= 1");
                    table.CheckConstraint("ck_containers_tare_weight", "tare_weight NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND tare_weight >= 0");
                    table.ForeignKey(
                        name: "fk_containers_created_by_account",
                        column: x => x.created_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_containers_deleted_by_account",
                        column: x => x.deleted_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customers",
                schema: "party",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_zh_tw = table.Column<string>(type: "text", nullable: true),
                    name_th_th = table.Column<string>(type: "text", nullable: true),
                    phone = table.Column<string>(type: "text", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_account_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customers", x => x.id);
                    table.CheckConstraint("ck_customers_deleted_pair", "(deleted_at IS NULL AND deleted_by_account_id IS NULL) OR (deleted_at IS NOT NULL AND deleted_by_account_id IS NOT NULL)");
                    table.CheckConstraint("ck_customers_name_present", "NULLIF(btrim(name_zh_tw), '') IS NOT NULL OR NULLIF(btrim(name_th_th), '') IS NOT NULL");
                    table.CheckConstraint("ck_customers_row_version", "row_version >= 1");
                    table.ForeignKey(
                        name: "fk_customers_created_by_account",
                        column: x => x.created_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_customers_deleted_by_account",
                        column: x => x.deleted_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "employees",
                schema: "party",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_zh_tw = table.Column<string>(type: "text", nullable: true),
                    name_th_th = table.Column<string>(type: "text", nullable: true),
                    bank_name = table.Column<string>(type: "text", nullable: true),
                    bank_account = table.Column<string>(type: "text", nullable: true),
                    phone = table.Column<string>(type: "text", nullable: true),
                    address = table.Column<string>(type: "text", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_account_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employees", x => x.id);
                    table.CheckConstraint("ck_employees_deleted_pair", "(deleted_at IS NULL AND deleted_by_account_id IS NULL) OR (deleted_at IS NOT NULL AND deleted_by_account_id IS NOT NULL)");
                    table.CheckConstraint("ck_employees_name_present", "NULLIF(btrim(name_zh_tw), '') IS NOT NULL OR NULLIF(btrim(name_th_th), '') IS NOT NULL");
                    table.CheckConstraint("ck_employees_row_version", "row_version >= 1");
                    table.ForeignKey(
                        name: "fk_employees_created_by_account",
                        column: x => x.created_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employees_deleted_by_account",
                        column: x => x.deleted_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "farmers",
                schema: "party",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_zh_tw = table.Column<string>(type: "text", nullable: true),
                    name_th_th = table.Column<string>(type: "text", nullable: true),
                    bank_name = table.Column<string>(type: "text", nullable: true),
                    bank_account = table.Column<string>(type: "text", nullable: true),
                    phone = table.Column<string>(type: "text", nullable: true),
                    address = table.Column<string>(type: "text", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_account_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_farmers", x => x.id);
                    table.CheckConstraint("ck_farmers_deleted_pair", "(deleted_at IS NULL AND deleted_by_account_id IS NULL) OR (deleted_at IS NOT NULL AND deleted_by_account_id IS NOT NULL)");
                    table.CheckConstraint("ck_farmers_name_present", "NULLIF(btrim(name_zh_tw), '') IS NOT NULL OR NULLIF(btrim(name_th_th), '') IS NOT NULL");
                    table.CheckConstraint("ck_farmers_row_version", "row_version >= 1");
                    table.ForeignKey(
                        name: "fk_farmers_created_by_account",
                        column: x => x.created_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_farmers_deleted_by_account",
                        column: x => x.deleted_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "outsourced_vendors",
                schema: "party",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_zh_tw = table.Column<string>(type: "text", nullable: true),
                    name_th_th = table.Column<string>(type: "text", nullable: true),
                    bank_name = table.Column<string>(type: "text", nullable: true),
                    bank_account = table.Column<string>(type: "text", nullable: true),
                    phone = table.Column<string>(type: "text", nullable: true),
                    address = table.Column<string>(type: "text", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_account_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outsourced_vendors", x => x.id);
                    table.CheckConstraint("ck_outsourced_vendors_deleted_pair", "(deleted_at IS NULL AND deleted_by_account_id IS NULL) OR (deleted_at IS NOT NULL AND deleted_by_account_id IS NOT NULL)");
                    table.CheckConstraint("ck_outsourced_vendors_name_present", "NULLIF(btrim(name_zh_tw), '') IS NOT NULL OR NULLIF(btrim(name_th_th), '') IS NOT NULL");
                    table.CheckConstraint("ck_outsourced_vendors_row_version", "row_version >= 1");
                    table.ForeignKey(
                        name: "fk_outsourced_vendors_created_by_account",
                        column: x => x.created_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_outsourced_vendors_deleted_by_account",
                        column: x => x.deleted_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_packaging_items",
                schema: "sales_handling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_zh_tw = table.Column<string>(type: "text", nullable: true),
                    name_th_th = table.Column<string>(type: "text", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_account_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_packaging_items", x => x.id);
                    table.CheckConstraint("ck_sales_packaging_items_deleted_pair", "(deleted_at IS NULL AND deleted_by_account_id IS NULL) OR (deleted_at IS NOT NULL AND deleted_by_account_id IS NOT NULL)");
                    table.CheckConstraint("ck_sales_packaging_items_name_present", "NULLIF(btrim(name_zh_tw), '') IS NOT NULL OR NULLIF(btrim(name_th_th), '') IS NOT NULL");
                    table.CheckConstraint("ck_sales_packaging_items_row_version", "row_version >= 1");
                    table.ForeignKey(
                        name: "fk_sales_packaging_items_created_by_account",
                        column: x => x.created_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_packaging_items_deleted_by_account",
                        column: x => x.deleted_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_product_groups",
                schema: "product",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_zh_tw = table.Column<string>(type: "text", nullable: true),
                    name_th_th = table.Column<string>(type: "text", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_account_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_product_groups", x => x.id);
                    table.CheckConstraint("ck_sales_product_groups_deleted_pair", "(deleted_at IS NULL AND deleted_by_account_id IS NULL) OR (deleted_at IS NOT NULL AND deleted_by_account_id IS NOT NULL)");
                    table.CheckConstraint("ck_sales_product_groups_name_present", "NULLIF(btrim(name_zh_tw), '') IS NOT NULL OR NULLIF(btrim(name_th_th), '') IS NOT NULL");
                    table.CheckConstraint("ck_sales_product_groups_row_version", "row_version >= 1");
                    table.ForeignKey(
                        name: "fk_sales_product_groups_created_by_account",
                        column: x => x.created_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_product_groups_deleted_by_account",
                        column: x => x.deleted_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "suppliers",
                schema: "party",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_zh_tw = table.Column<string>(type: "text", nullable: true),
                    name_th_th = table.Column<string>(type: "text", nullable: true),
                    bank_name = table.Column<string>(type: "text", nullable: true),
                    bank_account = table.Column<string>(type: "text", nullable: true),
                    phone = table.Column<string>(type: "text", nullable: true),
                    address = table.Column<string>(type: "text", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_account_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suppliers", x => x.id);
                    table.CheckConstraint("ck_suppliers_deleted_pair", "(deleted_at IS NULL AND deleted_by_account_id IS NULL) OR (deleted_at IS NOT NULL AND deleted_by_account_id IS NOT NULL)");
                    table.CheckConstraint("ck_suppliers_name_present", "NULLIF(btrim(name_zh_tw), '') IS NOT NULL OR NULLIF(btrim(name_th_th), '') IS NOT NULL");
                    table.CheckConstraint("ck_suppliers_row_version", "row_version >= 1");
                    table.ForeignKey(
                        name: "fk_suppliers_created_by_account",
                        column: x => x.created_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_suppliers_deleted_by_account",
                        column: x => x.deleted_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "warehouses",
                schema: "infrastructure",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "text", nullable: true),
                    name_zh_tw = table.Column<string>(type: "text", nullable: true),
                    name_th_th = table.Column<string>(type: "text", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_account_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_warehouses", x => x.id);
                    table.CheckConstraint("ck_warehouses_deleted_pair", "(deleted_at IS NULL AND deleted_by_account_id IS NULL) OR (deleted_at IS NOT NULL AND deleted_by_account_id IS NOT NULL)");
                    table.CheckConstraint("ck_warehouses_name_present", "NULLIF(btrim(name_zh_tw), '') IS NOT NULL OR NULLIF(btrim(name_th_th), '') IS NOT NULL");
                    table.CheckConstraint("ck_warehouses_row_version", "row_version >= 1");
                    table.ForeignKey(
                        name: "fk_warehouses_created_by_account",
                        column: x => x.created_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_warehouses_deleted_by_account",
                        column: x => x.deleted_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_event_subjects",
                schema: "audit",
                columns: table => new
                {
                    audit_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    subject_kind = table.Column<string>(type: "text", nullable: false),
                    subject_key = table.Column<JsonElement>(type: "jsonb", nullable: false),
                    change_kind = table.Column<string>(type: "text", nullable: false),
                    before_row_version = table.Column<long>(type: "bigint", nullable: true),
                    after_row_version = table.Column<long>(type: "bigint", nullable: true),
                    change_summary = table.Column<JsonElement>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_event_subjects", x => new { x.audit_event_id, x.sequence });
                    table.CheckConstraint("ck_audit_event_subjects_change_kind", "change_kind IN ('CREATE', 'UPDATE', 'SOFT_DELETE', 'RESTORE', 'HARD_DELETE')");
                    table.CheckConstraint("ck_audit_event_subjects_sequence", "sequence > 0");
                    table.CheckConstraint("ck_audit_event_subjects_subject_key_object", "jsonb_typeof(subject_key) = 'object'");
                    table.ForeignKey(
                        name: "fk_audit_event_subjects_event",
                        column: x => x.audit_event_id,
                        principalSchema: "audit",
                        principalTable: "audit_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "correction_links",
                schema: "audit",
                columns: table => new
                {
                    correction_audit_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    corrected_audit_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    correction_mode = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_correction_links", x => new { x.correction_audit_event_id, x.corrected_audit_event_id });
                    table.CheckConstraint("ck_correction_links_distinct_events", "correction_audit_event_id <> corrected_audit_event_id");
                    table.CheckConstraint("ck_correction_links_mode", "correction_mode IN ('DIRECT_AMENDMENT', 'COMPENSATION')");
                    table.ForeignKey(
                        name: "fk_correction_links_corrected_event",
                        column: x => x.corrected_audit_event_id,
                        principalSchema: "audit",
                        principalTable: "audit_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_correction_links_correction_event",
                        column: x => x.correction_audit_event_id,
                        principalSchema: "audit",
                        principalTable: "audit_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_date = table.Column<DateOnly>(type: "date", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmed_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_account_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales", x => x.id);
                    table.CheckConstraint("ck_sales_confirmation_state", "(status = 'DRAFT' AND confirmed_at IS NULL AND confirmed_by_account_id IS NULL) OR (status = 'CONFIRMED' AND confirmed_at IS NOT NULL AND confirmed_by_account_id IS NOT NULL)");
                    table.CheckConstraint("ck_sales_deleted_pair", "(deleted_at IS NULL AND deleted_by_account_id IS NULL) OR (deleted_at IS NOT NULL AND deleted_by_account_id IS NOT NULL)");
                    table.CheckConstraint("ck_sales_row_version", "row_version >= 1");
                    table.CheckConstraint("ck_sales_status", "status IN ('DRAFT', 'CONFIRMED')");
                    table.ForeignKey(
                        name: "fk_sales_confirmed_by_account",
                        column: x => x.confirmed_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_created_by_account",
                        column: x => x.created_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_customer",
                        column: x => x.customer_id,
                        principalSchema: "party",
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_deleted_by_account",
                        column: x => x.deleted_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "employee_daily_wages",
                schema: "labor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_date = table.Column<DateOnly>(type: "date", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    processing_wage_total_thb = table.Column<long>(type: "bigint", nullable: false),
                    sales_packaging_wage_total_thb = table.Column<long>(type: "bigint", nullable: false),
                    total_wage_thb = table.Column<long>(type: "bigint", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_daily_wages", x => x.id);
                    table.CheckConstraint("ck_employee_daily_wages_packaging_total", "sales_packaging_wage_total_thb >= 0");
                    table.CheckConstraint("ck_employee_daily_wages_processing_total", "processing_wage_total_thb >= 0");
                    table.CheckConstraint("ck_employee_daily_wages_row_version", "row_version >= 1");
                    table.CheckConstraint("ck_employee_daily_wages_total", "total_wage_thb >= 0");
                    table.CheckConstraint("ck_employee_daily_wages_total_formula", "total_wage_thb = processing_wage_total_thb + sales_packaging_wage_total_thb");
                    table.ForeignKey(
                        name: "fk_employee_daily_wages_confirmed_by_account",
                        column: x => x.confirmed_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employee_daily_wages_employee",
                        column: x => x.employee_id,
                        principalSchema: "party",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "outsourced_supply_batches",
                schema: "outsourced",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    supply_date = table.Column<DateOnly>(type: "date", nullable: false),
                    outsourced_vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lifecycle_status = table.Column<string>(type: "text", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_account_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outsourced_supply_batches", x => x.id);
                    table.CheckConstraint("ck_outsourced_supply_batches_closing_state", "(lifecycle_status = 'ACTIVE' AND closed_at IS NULL AND closed_by_account_id IS NULL) OR (lifecycle_status = 'CLOSED' AND closed_at IS NOT NULL AND closed_by_account_id IS NOT NULL)");
                    table.CheckConstraint("ck_outsourced_supply_batches_deleted_pair", "(deleted_at IS NULL AND deleted_by_account_id IS NULL) OR (deleted_at IS NOT NULL AND deleted_by_account_id IS NOT NULL)");
                    table.CheckConstraint("ck_outsourced_supply_batches_lifecycle_status", "lifecycle_status IN ('ACTIVE', 'CLOSED')");
                    table.CheckConstraint("ck_outsourced_supply_batches_row_version", "row_version >= 1");
                    table.ForeignKey(
                        name: "fk_outsourced_supply_batches_closed_by_account",
                        column: x => x.closed_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_outsourced_supply_batches_created_by_account",
                        column: x => x.created_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_outsourced_supply_batches_deleted_by_account",
                        column: x => x.deleted_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_outsourced_supply_batches_vendor",
                        column: x => x.outsourced_vendor_id,
                        principalSchema: "party",
                        principalTable: "outsourced_vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "storage_locations",
                schema: "infrastructure",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "text", nullable: true),
                    name_zh_tw = table.Column<string>(type: "text", nullable: true),
                    name_th_th = table.Column<string>(type: "text", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_account_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_storage_locations", x => x.id);
                    table.CheckConstraint("ck_storage_locations_deleted_pair", "(deleted_at IS NULL AND deleted_by_account_id IS NULL) OR (deleted_at IS NOT NULL AND deleted_by_account_id IS NOT NULL)");
                    table.CheckConstraint("ck_storage_locations_name_present", "NULLIF(btrim(name_zh_tw), '') IS NOT NULL OR NULLIF(btrim(name_th_th), '') IS NOT NULL");
                    table.CheckConstraint("ck_storage_locations_row_version", "row_version >= 1");
                    table.ForeignKey(
                        name: "fk_storage_locations_created_by_account",
                        column: x => x.created_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_storage_locations_deleted_by_account",
                        column: x => x.deleted_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_storage_locations_warehouse",
                        column: x => x.warehouse_id,
                        principalSchema: "infrastructure",
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "receivables",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receivables", x => x.id);
                    table.UniqueConstraint("ak_receivables_id_sales", x => new { x.id, x.sales_id });
                    table.CheckConstraint("ck_receivables_row_version", "row_version >= 1");
                    table.ForeignKey(
                        name: "fk_receivables_sales",
                        column: x => x.sales_id,
                        principalSchema: "sales",
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_allocation_revisions",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_allocation_revisions", x => x.id);
                    table.UniqueConstraint("ak_sales_allocation_revisions_id_sales", x => new { x.id, x.sales_id });
                    table.CheckConstraint("ck_sales_allocation_revisions_revision_number", "revision_number >= 0");
                    table.ForeignKey(
                        name: "fk_sales_allocation_revisions_created_by_account",
                        column: x => x.created_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_allocation_revisions_sales",
                        column: x => x.sales_id,
                        principalSchema: "sales",
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_packaging_work_records",
                schema: "sales_handling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_date = table.Column<DateOnly>(type: "date", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_packaging_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    confirmed_wage_thb = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_account_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_packaging_work_records", x => x.id);
                    table.CheckConstraint("ck_sales_packaging_work_records_confirmed_wage", "confirmed_wage_thb >= 0");
                    table.CheckConstraint("ck_sales_packaging_work_records_deleted_pair", "(deleted_at IS NULL AND deleted_by_account_id IS NULL) OR (deleted_at IS NOT NULL AND deleted_by_account_id IS NOT NULL)");
                    table.CheckConstraint("ck_sales_packaging_work_records_row_version", "row_version >= 1");
                    table.ForeignKey(
                        name: "fk_sales_packaging_work_records_deleted_by_account",
                        column: x => x.deleted_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_packaging_work_records_employee",
                        column: x => x.employee_id,
                        principalSchema: "party",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_packaging_work_records_item",
                        column: x => x.sales_packaging_item_id,
                        principalSchema: "sales_handling",
                        principalTable: "sales_packaging_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_packaging_work_records_recorded_by_account",
                        column: x => x.recorded_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_packaging_work_records_sales",
                        column: x => x.sales_id,
                        principalSchema: "sales",
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "procurement_products",
                schema: "product",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_zh_tw = table.Column<string>(type: "text", nullable: true),
                    name_th_th = table.Column<string>(type: "text", nullable: true),
                    unit_code = table.Column<string>(type: "text", nullable: false),
                    default_storage_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_account_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_procurement_products", x => x.id);
                    table.CheckConstraint("ck_procurement_products_deleted_pair", "(deleted_at IS NULL AND deleted_by_account_id IS NULL) OR (deleted_at IS NOT NULL AND deleted_by_account_id IS NOT NULL)");
                    table.CheckConstraint("ck_procurement_products_name_present", "NULLIF(btrim(name_zh_tw), '') IS NOT NULL OR NULLIF(btrim(name_th_th), '') IS NOT NULL");
                    table.CheckConstraint("ck_procurement_products_row_version", "row_version >= 1");
                    table.CheckConstraint("ck_procurement_products_unit_code", "NULLIF(btrim(unit_code), '') IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_procurement_products_created_by_account",
                        column: x => x.created_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_procurement_products_default_storage_location",
                        column: x => x.default_storage_location_id,
                        principalSchema: "infrastructure",
                        principalTable: "storage_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_procurement_products_deleted_by_account",
                        column: x => x.deleted_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_products",
                schema: "product",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_product_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_zh_tw = table.Column<string>(type: "text", nullable: true),
                    name_th_th = table.Column<string>(type: "text", nullable: true),
                    pricing_basis = table.Column<string>(type: "text", nullable: false),
                    packaging_weight = table.Column<decimal>(type: "numeric", nullable: true),
                    sales_weight = table.Column<decimal>(type: "numeric", nullable: true),
                    default_storage_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_account_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_products", x => x.id);
                    table.CheckConstraint("ck_sales_products_deleted_pair", "(deleted_at IS NULL AND deleted_by_account_id IS NULL) OR (deleted_at IS NOT NULL AND deleted_by_account_id IS NOT NULL)");
                    table.CheckConstraint("ck_sales_products_name_present", "NULLIF(btrim(name_zh_tw), '') IS NOT NULL OR NULLIF(btrim(name_th_th), '') IS NOT NULL");
                    table.CheckConstraint("ck_sales_products_packaging_weight", "packaging_weight IS NULL OR (packaging_weight NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND packaging_weight > 0)");
                    table.CheckConstraint("ck_sales_products_pricing_basis", "pricing_basis IN ('WEIGHT_BASED_UNIT', 'UNIT_BASED')");
                    table.CheckConstraint("ck_sales_products_pricing_shape", "(pricing_basis = 'WEIGHT_BASED_UNIT' AND sales_weight IS NOT NULL) OR (pricing_basis = 'UNIT_BASED' AND sales_weight IS NULL)");
                    table.CheckConstraint("ck_sales_products_row_version", "row_version >= 1");
                    table.CheckConstraint("ck_sales_products_sales_weight", "sales_weight IS NULL OR (sales_weight NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND sales_weight >= 0)");
                    table.ForeignKey(
                        name: "fk_sales_products_created_by_account",
                        column: x => x.created_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_products_default_storage_location",
                        column: x => x.default_storage_location_id,
                        principalSchema: "infrastructure",
                        principalTable: "storage_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_products_deleted_by_account",
                        column: x => x.deleted_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_products_group",
                        column: x => x.sales_product_group_id,
                        principalSchema: "product",
                        principalTable: "sales_product_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "receipts",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    receivable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_thb = table.Column<long>(type: "bigint", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_by_account_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receipts", x => x.id);
                    table.CheckConstraint("ck_receipts_amount", "amount_thb > 0");
                    table.ForeignKey(
                        name: "fk_receipts_confirmed_by_account",
                        column: x => x.confirmed_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_receipts_receivable",
                        column: x => x.receivable_id,
                        principalSchema: "finance",
                        principalTable: "receivables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "receivable_outstanding_positions",
                schema: "finance",
                columns: table => new
                {
                    receivable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_obligation_thb = table.Column<long>(type: "bigint", nullable: false),
                    adjustment_total_thb = table.Column<long>(type: "bigint", nullable: false),
                    settlement_total_thb = table.Column<long>(type: "bigint", nullable: false),
                    outstanding_thb = table.Column<long>(type: "bigint", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receivable_outstanding_positions", x => x.receivable_id);
                    table.CheckConstraint("ck_receivable_outstanding_positions_formula", "outstanding_thb = original_obligation_thb + adjustment_total_thb - settlement_total_thb");
                    table.CheckConstraint("ck_receivable_outstanding_positions_original", "original_obligation_thb >= 0");
                    table.CheckConstraint("ck_receivable_outstanding_positions_row_version", "row_version >= 1");
                    table.CheckConstraint("ck_receivable_outstanding_positions_settlement", "settlement_total_thb >= 0");
                    table.ForeignKey(
                        name: "fk_receivable_outstanding_positions_receivable",
                        column: x => x.receivable_id,
                        principalSchema: "finance",
                        principalTable: "receivables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_packaging_wage_components",
                schema: "labor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_daily_wage_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_packaging_work_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    wage_amount_thb = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_packaging_wage_components", x => x.id);
                    table.CheckConstraint("ck_sales_packaging_wage_components_amount", "wage_amount_thb >= 0");
                    table.ForeignKey(
                        name: "fk_sales_packaging_wage_components_daily_wage",
                        column: x => x.employee_daily_wage_id,
                        principalSchema: "labor",
                        principalTable: "employee_daily_wages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_packaging_wage_components_work_record",
                        column: x => x.sales_packaging_work_record_id,
                        principalSchema: "sales_handling",
                        principalTable: "sales_packaging_work_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "processing_routes",
                schema: "processing_config",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    procurement_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_zh_tw = table.Column<string>(type: "text", nullable: true),
                    name_th_th = table.Column<string>(type: "text", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_account_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processing_routes", x => x.id);
                    table.CheckConstraint("ck_processing_routes_deleted_pair", "(deleted_at IS NULL AND deleted_by_account_id IS NULL) OR (deleted_at IS NOT NULL AND deleted_by_account_id IS NOT NULL)");
                    table.CheckConstraint("ck_processing_routes_name_present", "NULLIF(btrim(name_zh_tw), '') IS NOT NULL OR NULLIF(btrim(name_th_th), '') IS NOT NULL");
                    table.CheckConstraint("ck_processing_routes_row_version", "row_version >= 1");
                    table.ForeignKey(
                        name: "fk_processing_routes_created_by_account",
                        column: x => x.created_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_processing_routes_deleted_by_account",
                        column: x => x.deleted_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_processing_routes_procurement_product",
                        column: x => x.procurement_product_id,
                        principalSchema: "product",
                        principalTable: "procurement_products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "outsourced_supply_details",
                schema: "outsourced",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    outsourced_supply_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    pricing_basis_snapshot = table.Column<string>(type: "text", nullable: false),
                    sales_weight_snapshot = table.Column<decimal>(type: "numeric", nullable: true),
                    unit_price = table.Column<decimal>(type: "numeric", nullable: false),
                    amount_thb = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_account_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outsourced_supply_details", x => x.id);
                    table.CheckConstraint("ck_outsourced_supply_details_amount", "(pricing_basis_snapshot = 'WEIGHT_BASED_UNIT' AND amount_thb = floor(quantity * sales_weight_snapshot * unit_price)) OR (pricing_basis_snapshot = 'UNIT_BASED' AND amount_thb = floor(quantity * unit_price))");
                    table.CheckConstraint("ck_outsourced_supply_details_deleted_pair", "(deleted_at IS NULL AND deleted_by_account_id IS NULL) OR (deleted_at IS NOT NULL AND deleted_by_account_id IS NOT NULL)");
                    table.CheckConstraint("ck_outsourced_supply_details_pricing_basis", "pricing_basis_snapshot IN ('WEIGHT_BASED_UNIT', 'UNIT_BASED')");
                    table.CheckConstraint("ck_outsourced_supply_details_pricing_shape", "(pricing_basis_snapshot = 'WEIGHT_BASED_UNIT' AND sales_weight_snapshot IS NOT NULL) OR (pricing_basis_snapshot = 'UNIT_BASED' AND sales_weight_snapshot IS NULL)");
                    table.CheckConstraint("ck_outsourced_supply_details_quantity", "quantity NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND quantity >= 0");
                    table.CheckConstraint("ck_outsourced_supply_details_row_version", "row_version >= 1");
                    table.CheckConstraint("ck_outsourced_supply_details_sales_weight", "sales_weight_snapshot IS NULL OR (sales_weight_snapshot NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND sales_weight_snapshot >= 0)");
                    table.CheckConstraint("ck_outsourced_supply_details_unit_price", "unit_price NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND unit_price >= 0");
                    table.ForeignKey(
                        name: "fk_outsourced_supply_details_batch",
                        column: x => x.outsourced_supply_batch_id,
                        principalSchema: "outsourced",
                        principalTable: "outsourced_supply_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_outsourced_supply_details_deleted_by_account",
                        column: x => x.deleted_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_outsourced_supply_details_recorded_by_account",
                        column: x => x.recorded_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_outsourced_supply_details_sales_product",
                        column: x => x.sales_product_id,
                        principalSchema: "product",
                        principalTable: "sales_products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_details",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    sales_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    pricing_basis_snapshot = table.Column<string>(type: "text", nullable: false),
                    sales_weight_snapshot = table.Column<decimal>(type: "numeric", nullable: true),
                    unit_price = table.Column<decimal>(type: "numeric", nullable: false),
                    amount_thb = table.Column<long>(type: "bigint", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_account_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_details", x => x.id);
                    table.UniqueConstraint("ak_sales_details_id_sales", x => new { x.id, x.sales_id });
                    table.CheckConstraint("ck_sales_details_amount", "(pricing_basis_snapshot = 'WEIGHT_BASED_UNIT' AND amount_thb = floor(quantity * sales_weight_snapshot * unit_price)) OR (pricing_basis_snapshot = 'UNIT_BASED' AND amount_thb = floor(quantity * unit_price))");
                    table.CheckConstraint("ck_sales_details_deleted_pair", "(deleted_at IS NULL AND deleted_by_account_id IS NULL) OR (deleted_at IS NOT NULL AND deleted_by_account_id IS NOT NULL)");
                    table.CheckConstraint("ck_sales_details_line_number", "line_number > 0");
                    table.CheckConstraint("ck_sales_details_pricing_basis", "pricing_basis_snapshot IN ('WEIGHT_BASED_UNIT', 'UNIT_BASED')");
                    table.CheckConstraint("ck_sales_details_pricing_shape", "(pricing_basis_snapshot = 'WEIGHT_BASED_UNIT' AND sales_weight_snapshot IS NOT NULL) OR (pricing_basis_snapshot = 'UNIT_BASED' AND sales_weight_snapshot IS NULL)");
                    table.CheckConstraint("ck_sales_details_quantity", "quantity NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND quantity >= 0");
                    table.CheckConstraint("ck_sales_details_row_version", "row_version >= 1");
                    table.CheckConstraint("ck_sales_details_sales_weight", "sales_weight_snapshot IS NULL OR (sales_weight_snapshot NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND sales_weight_snapshot >= 0)");
                    table.CheckConstraint("ck_sales_details_unit_price", "unit_price NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND unit_price >= 0");
                    table.ForeignKey(
                        name: "fk_sales_details_created_by_account",
                        column: x => x.created_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_details_deleted_by_account",
                        column: x => x.deleted_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_details_sales",
                        column: x => x.sales_id,
                        principalSchema: "sales",
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_details_sales_product",
                        column: x => x.sales_product_id,
                        principalSchema: "product",
                        principalTable: "sales_products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "processing_route_versions",
                schema: "processing_config",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    processing_route_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processing_route_versions", x => x.id);
                    table.UniqueConstraint("ak_processing_route_versions_id_route", x => new { x.id, x.processing_route_id });
                    table.CheckConstraint("ck_processing_route_versions_status", "status IN ('DRAFT', 'VALIDATED', 'ACTIVE', 'RETIRED')");
                    table.CheckConstraint("ck_processing_route_versions_version_number", "version_number > 0");
                    table.ForeignKey(
                        name: "fk_processing_route_versions_route",
                        column: x => x.processing_route_id,
                        principalSchema: "processing_config",
                        principalTable: "processing_routes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "receivable_obligation_items",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    receivable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_detail_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_thb = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receivable_obligation_items", x => x.id);
                    table.CheckConstraint("ck_receivable_obligation_items_amount", "amount_thb >= 0");
                    table.ForeignKey(
                        name: "fk_receivable_obligation_items_detail_sales",
                        columns: x => new { x.sales_detail_id, x.sales_id },
                        principalSchema: "sales",
                        principalTable: "sales_details",
                        principalColumns: new[] { "id", "sales_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_receivable_obligation_items_receivable_sales",
                        columns: x => new { x.receivable_id, x.sales_id },
                        principalSchema: "finance",
                        principalTable: "receivables",
                        principalColumns: new[] { "id", "sales_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "process_materials",
                schema: "processing_config",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    processing_route_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_zh_tw = table.Column<string>(type: "text", nullable: true),
                    name_th_th = table.Column<string>(type: "text", nullable: true),
                    uses_container = table.Column<bool>(type: "boolean", nullable: false),
                    container_id = table.Column<Guid>(type: "uuid", nullable: true),
                    default_container_count = table.Column<int>(type: "integer", nullable: true),
                    default_storage_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_account_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_process_materials", x => x.id);
                    table.UniqueConstraint("ak_process_materials_id_route_version", x => new { x.id, x.processing_route_version_id });
                    table.CheckConstraint("ck_process_materials_container_count", "default_container_count IS NULL OR default_container_count >= 0");
                    table.CheckConstraint("ck_process_materials_container_shape", "(uses_container AND container_id IS NOT NULL AND default_container_count IS NOT NULL) OR (NOT uses_container AND container_id IS NULL AND default_container_count IS NULL)");
                    table.CheckConstraint("ck_process_materials_deleted_pair", "(deleted_at IS NULL AND deleted_by_account_id IS NULL) OR (deleted_at IS NOT NULL AND deleted_by_account_id IS NOT NULL)");
                    table.CheckConstraint("ck_process_materials_name_present", "NULLIF(btrim(name_zh_tw), '') IS NOT NULL OR NULLIF(btrim(name_th_th), '') IS NOT NULL");
                    table.CheckConstraint("ck_process_materials_row_version", "row_version >= 1");
                    table.ForeignKey(
                        name: "fk_process_materials_container",
                        column: x => x.container_id,
                        principalSchema: "infrastructure",
                        principalTable: "containers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_process_materials_created_by_account",
                        column: x => x.created_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_process_materials_default_storage_location",
                        column: x => x.default_storage_location_id,
                        principalSchema: "infrastructure",
                        principalTable: "storage_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_process_materials_deleted_by_account",
                        column: x => x.deleted_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_process_materials_route_version",
                        column: x => x.processing_route_version_id,
                        principalSchema: "processing_config",
                        principalTable: "processing_route_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "procurement_batches",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    procurement_date = table.Column<DateOnly>(type: "date", nullable: false),
                    procurement_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    procurement_status = table.Column<string>(type: "text", nullable: false),
                    lifecycle_status = table.Column<string>(type: "text", nullable: false),
                    processing_route_id = table.Column<Guid>(type: "uuid", nullable: true),
                    processing_route_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_account_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_procurement_batches", x => x.id);
                    table.CheckConstraint("ck_procurement_batches_closing_state", "(lifecycle_status = 'ACTIVE' AND closed_at IS NULL AND closed_by_account_id IS NULL) OR (lifecycle_status = 'CLOSED' AND closed_at IS NOT NULL AND closed_by_account_id IS NOT NULL)");
                    table.CheckConstraint("ck_procurement_batches_completion_state", "(procurement_status = 'OPEN' AND completed_at IS NULL AND completed_by_account_id IS NULL) OR (procurement_status = 'COMPLETED' AND completed_at IS NOT NULL AND completed_by_account_id IS NOT NULL)");
                    table.CheckConstraint("ck_procurement_batches_deleted_pair", "(deleted_at IS NULL AND deleted_by_account_id IS NULL) OR (deleted_at IS NOT NULL AND deleted_by_account_id IS NOT NULL)");
                    table.CheckConstraint("ck_procurement_batches_lifecycle_status", "lifecycle_status IN ('ACTIVE', 'CLOSED')");
                    table.CheckConstraint("ck_procurement_batches_route_binding", "(processing_route_id IS NULL AND processing_route_version_id IS NULL) OR (processing_route_id IS NOT NULL AND processing_route_version_id IS NOT NULL)");
                    table.CheckConstraint("ck_procurement_batches_row_version", "row_version >= 1");
                    table.CheckConstraint("ck_procurement_batches_status", "procurement_status IN ('OPEN', 'COMPLETED')");
                    table.ForeignKey(
                        name: "fk_procurement_batches_closed_by_account",
                        column: x => x.closed_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_procurement_batches_completed_by_account",
                        column: x => x.completed_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_procurement_batches_created_by_account",
                        column: x => x.created_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_procurement_batches_deleted_by_account",
                        column: x => x.deleted_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_procurement_batches_product",
                        column: x => x.procurement_product_id,
                        principalSchema: "product",
                        principalTable: "procurement_products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_procurement_batches_route",
                        column: x => x.processing_route_id,
                        principalSchema: "processing_config",
                        principalTable: "processing_routes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_procurement_batches_route_version_route",
                        columns: x => new { x.processing_route_version_id, x.processing_route_id },
                        principalSchema: "processing_config",
                        principalTable: "processing_route_versions",
                        principalColumns: new[] { "id", "processing_route_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "route_input_configs",
                schema: "processing_config",
                columns: table => new
                {
                    processing_route_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uses_container = table.Column<bool>(type: "boolean", nullable: false),
                    container_id = table.Column<Guid>(type: "uuid", nullable: true),
                    default_container_count = table.Column<int>(type: "integer", nullable: true),
                    default_storage_location_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_route_input_configs", x => x.processing_route_version_id);
                    table.CheckConstraint("ck_route_input_configs_container_count", "default_container_count IS NULL OR default_container_count >= 0");
                    table.CheckConstraint("ck_route_input_configs_container_shape", "(uses_container AND container_id IS NOT NULL AND default_container_count IS NOT NULL) OR (NOT uses_container AND container_id IS NULL AND default_container_count IS NULL)");
                    table.ForeignKey(
                        name: "fk_route_input_configs_container",
                        column: x => x.container_id,
                        principalSchema: "infrastructure",
                        principalTable: "containers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_route_input_configs_default_storage_location",
                        column: x => x.default_storage_location_id,
                        principalSchema: "infrastructure",
                        principalTable: "storage_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_route_input_configs_route_version",
                        column: x => x.processing_route_version_id,
                        principalSchema: "processing_config",
                        principalTable: "processing_route_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "processing_modules",
                schema: "processing_config",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    processing_route_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_zh_tw = table.Column<string>(type: "text", nullable: true),
                    name_th_th = table.Column<string>(type: "text", nullable: true),
                    execution_mode = table.Column<string>(type: "text", nullable: false),
                    input_process_material_id = table.Column<Guid>(type: "uuid", nullable: true),
                    negative_inventory_policy = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processing_modules", x => x.id);
                    table.UniqueConstraint("ak_processing_modules_id_route_version", x => new { x.id, x.processing_route_version_id });
                    table.CheckConstraint("ck_processing_modules_execution_mode", "execution_mode IN ('SOURCE_TRACKED', 'POOLED_OUTPUT', 'FINAL_PACKAGING')");
                    table.CheckConstraint("ck_processing_modules_name_present", "NULLIF(btrim(name_zh_tw), '') IS NOT NULL OR NULLIF(btrim(name_th_th), '') IS NOT NULL");
                    table.CheckConstraint("ck_processing_modules_negative_inventory_policy", "NULLIF(btrim(negative_inventory_policy), '') IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_processing_modules_input_material_route_version",
                        columns: x => new { x.input_process_material_id, x.processing_route_version_id },
                        principalSchema: "processing_config",
                        principalTable: "process_materials",
                        principalColumns: new[] { "id", "processing_route_version_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_processing_modules_route_version",
                        column: x => x.processing_route_version_id,
                        principalSchema: "processing_config",
                        principalTable: "processing_route_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_positions",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    origin = table.Column<string>(type: "text", nullable: false),
                    procurement_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    outsourced_supply_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inventory_object_kind = table.Column<string>(type: "text", nullable: false),
                    procurement_product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    process_material_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sales_product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    storage_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    raw_source_kind = table.Column<string>(type: "text", nullable: true),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    balance_quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_positions", x => x.id);
                    table.CheckConstraint("ck_inventory_positions_balance_quantity", "balance_quantity NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric)");
                    table.CheckConstraint("ck_inventory_positions_object_kind", "inventory_object_kind IN ('PROCUREMENT_PRODUCT', 'PROCESS_MATERIAL', 'SALES_PRODUCT')");
                    table.CheckConstraint("ck_inventory_positions_origin", "origin IN ('IN_HOUSE', 'OUTSOURCED')");
                    table.CheckConstraint("ck_inventory_positions_raw_source_kind", "raw_source_kind IS NULL OR raw_source_kind IN ('SUPPLIER', 'FARMERS_COMBINED')");
                    table.CheckConstraint("ck_inventory_positions_raw_source_shape", "(raw_source_kind IS NULL AND supplier_id IS NULL) OR (origin = 'IN_HOUSE' AND inventory_object_kind = 'PROCUREMENT_PRODUCT' AND raw_source_kind = 'SUPPLIER' AND supplier_id IS NOT NULL) OR (origin = 'IN_HOUSE' AND inventory_object_kind = 'PROCUREMENT_PRODUCT' AND raw_source_kind = 'FARMERS_COMBINED' AND supplier_id IS NULL)");
                    table.CheckConstraint("ck_inventory_positions_row_version", "row_version >= 1");
                    table.CheckConstraint("ck_inventory_positions_source_batch", "(origin = 'IN_HOUSE' AND procurement_batch_id IS NOT NULL AND outsourced_supply_batch_id IS NULL) OR (origin = 'OUTSOURCED' AND procurement_batch_id IS NULL AND outsourced_supply_batch_id IS NOT NULL)");
                    table.CheckConstraint("ck_inventory_positions_typed_object", "(inventory_object_kind = 'PROCUREMENT_PRODUCT' AND procurement_product_id IS NOT NULL AND process_material_id IS NULL AND sales_product_id IS NULL) OR (inventory_object_kind = 'PROCESS_MATERIAL' AND procurement_product_id IS NULL AND process_material_id IS NOT NULL AND sales_product_id IS NULL) OR (inventory_object_kind = 'SALES_PRODUCT' AND procurement_product_id IS NULL AND process_material_id IS NULL AND sales_product_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_inventory_positions_outsourced_batch",
                        column: x => x.outsourced_supply_batch_id,
                        principalSchema: "outsourced",
                        principalTable: "outsourced_supply_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_positions_process_material",
                        column: x => x.process_material_id,
                        principalSchema: "processing_config",
                        principalTable: "process_materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_positions_procurement_batch",
                        column: x => x.procurement_batch_id,
                        principalSchema: "procurement",
                        principalTable: "procurement_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_positions_procurement_product",
                        column: x => x.procurement_product_id,
                        principalSchema: "product",
                        principalTable: "procurement_products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_positions_sales_product",
                        column: x => x.sales_product_id,
                        principalSchema: "product",
                        principalTable: "sales_products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_positions_storage_location",
                        column: x => x.storage_location_id,
                        principalSchema: "infrastructure",
                        principalTable: "storage_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_positions_supplier",
                        column: x => x.supplier_id,
                        principalSchema: "party",
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payables",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payable_kind = table.Column<string>(type: "text", nullable: false),
                    procurement_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    farmer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    outsourced_supply_detail_id = table.Column<Guid>(type: "uuid", nullable: true),
                    employee_daily_wage_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payables", x => x.id);
                    table.UniqueConstraint("ak_payables_id_kind", x => new { x.id, x.payable_kind });
                    table.CheckConstraint("ck_payables_kind", "payable_kind IN ('PROCUREMENT_SUPPLIER', 'PROCUREMENT_FARMER', 'COMPANY_PICKUP_TRANSPORT', 'OUTSOURCED_VENDOR', 'EMPLOYEE_DAILY_WAGE')");
                    table.CheckConstraint("ck_payables_row_version", "row_version >= 1");
                    table.CheckConstraint("ck_payables_typed_source", "(payable_kind = 'PROCUREMENT_SUPPLIER' AND procurement_batch_id IS NOT NULL AND supplier_id IS NOT NULL AND farmer_id IS NULL AND outsourced_supply_detail_id IS NULL AND employee_daily_wage_id IS NULL) OR (payable_kind = 'PROCUREMENT_FARMER' AND procurement_batch_id IS NOT NULL AND supplier_id IS NULL AND farmer_id IS NOT NULL AND outsourced_supply_detail_id IS NULL AND employee_daily_wage_id IS NULL) OR (payable_kind = 'COMPANY_PICKUP_TRANSPORT' AND procurement_batch_id IS NULL AND supplier_id IS NULL AND farmer_id IS NULL AND outsourced_supply_detail_id IS NULL AND employee_daily_wage_id IS NULL) OR (payable_kind = 'OUTSOURCED_VENDOR' AND procurement_batch_id IS NULL AND supplier_id IS NULL AND farmer_id IS NULL AND outsourced_supply_detail_id IS NOT NULL AND employee_daily_wage_id IS NULL) OR (payable_kind = 'EMPLOYEE_DAILY_WAGE' AND procurement_batch_id IS NULL AND supplier_id IS NULL AND farmer_id IS NULL AND outsourced_supply_detail_id IS NULL AND employee_daily_wage_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_payables_employee_daily_wage",
                        column: x => x.employee_daily_wage_id,
                        principalSchema: "labor",
                        principalTable: "employee_daily_wages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payables_farmer",
                        column: x => x.farmer_id,
                        principalSchema: "party",
                        principalTable: "farmers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payables_outsourced_supply_detail",
                        column: x => x.outsourced_supply_detail_id,
                        principalSchema: "outsourced",
                        principalTable: "outsourced_supply_details",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payables_procurement_batch",
                        column: x => x.procurement_batch_id,
                        principalSchema: "procurement",
                        principalTable: "procurement_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payables_supplier",
                        column: x => x.supplier_id,
                        principalSchema: "party",
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "procurement_entries",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    procurement_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_type = table.Column<string>(type: "text", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    farmer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    net_quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    unit_code_snapshot = table.Column<string>(type: "text", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric", nullable: false),
                    amount_thb = table.Column<long>(type: "bigint", nullable: false),
                    company_pickup = table.Column<bool>(type: "boolean", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_account_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_procurement_entries", x => x.id);
                    table.CheckConstraint("ck_procurement_entries_amount", "amount_thb = floor(net_quantity * unit_price)");
                    table.CheckConstraint("ck_procurement_entries_deleted_pair", "(deleted_at IS NULL AND deleted_by_account_id IS NULL) OR (deleted_at IS NOT NULL AND deleted_by_account_id IS NOT NULL)");
                    table.CheckConstraint("ck_procurement_entries_net_quantity", "net_quantity NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND net_quantity >= 0");
                    table.CheckConstraint("ck_procurement_entries_row_version", "row_version >= 1");
                    table.CheckConstraint("ck_procurement_entries_source_type", "source_type IN ('SUPPLIER', 'FARMER')");
                    table.CheckConstraint("ck_procurement_entries_typed_source", "(source_type = 'SUPPLIER' AND supplier_id IS NOT NULL AND farmer_id IS NULL) OR (source_type = 'FARMER' AND supplier_id IS NULL AND farmer_id IS NOT NULL)");
                    table.CheckConstraint("ck_procurement_entries_unit_code_snapshot", "NULLIF(btrim(unit_code_snapshot), '') IS NOT NULL");
                    table.CheckConstraint("ck_procurement_entries_unit_price", "unit_price NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND unit_price >= 0");
                    table.ForeignKey(
                        name: "fk_procurement_entries_batch",
                        column: x => x.procurement_batch_id,
                        principalSchema: "procurement",
                        principalTable: "procurement_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_procurement_entries_deleted_by_account",
                        column: x => x.deleted_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_procurement_entries_farmer",
                        column: x => x.farmer_id,
                        principalSchema: "party",
                        principalTable: "farmers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_procurement_entries_recorded_by_account",
                        column: x => x.recorded_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_procurement_entries_supplier",
                        column: x => x.supplier_id,
                        principalSchema: "party",
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_allocation_revision_items",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_allocation_revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_detail_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    origin = table.Column<string>(type: "text", nullable: false),
                    procurement_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    outsourced_supply_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    allocated_quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    manual_override = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_allocation_revision_items", x => x.id);
                    table.UniqueConstraint("ak_sales_allocation_revision_items_id_sales_detail", x => new { x.id, x.sales_detail_id });
                    table.CheckConstraint("ck_sales_allocation_revision_items_allocated_quantity", "allocated_quantity NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND allocated_quantity >= 0");
                    table.CheckConstraint("ck_sales_allocation_revision_items_origin", "origin IN ('IN_HOUSE', 'OUTSOURCED')");
                    table.CheckConstraint("ck_sales_allocation_revision_items_sequence", "sequence > 0");
                    table.CheckConstraint("ck_sales_allocation_revision_items_typed_source_batch", "(origin = 'IN_HOUSE' AND procurement_batch_id IS NOT NULL AND outsourced_supply_batch_id IS NULL) OR (origin = 'OUTSOURCED' AND procurement_batch_id IS NULL AND outsourced_supply_batch_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_sales_allocation_revision_items_detail_sales",
                        columns: x => new { x.sales_detail_id, x.sales_id },
                        principalSchema: "sales",
                        principalTable: "sales_details",
                        principalColumns: new[] { "id", "sales_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_allocation_revision_items_outsourced_batch",
                        column: x => x.outsourced_supply_batch_id,
                        principalSchema: "outsourced",
                        principalTable: "outsourced_supply_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_allocation_revision_items_procurement_batch",
                        column: x => x.procurement_batch_id,
                        principalSchema: "procurement",
                        principalTable: "procurement_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_allocation_revision_items_revision_sales",
                        columns: x => new { x.sales_allocation_revision_id, x.sales_id },
                        principalSchema: "sales",
                        principalTable: "sales_allocation_revisions",
                        principalColumns: new[] { "id", "sales_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_allocation_revision_items_sales",
                        column: x => x.sales_id,
                        principalSchema: "sales",
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "processing_executions",
                schema: "processing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_date = table.Column<DateOnly>(type: "date", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    procurement_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    processing_route_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    processing_module_id = table.Column<Guid>(type: "uuid", nullable: false),
                    execution_mode_snapshot = table.Column<string>(type: "text", nullable: false),
                    negative_inventory_policy_snapshot = table.Column<string>(type: "text", nullable: false),
                    processing_source_kind = table.Column<string>(type: "text", nullable: true),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_account_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processing_executions", x => x.id);
                    table.CheckConstraint("ck_processing_executions_deleted_pair", "(deleted_at IS NULL AND deleted_by_account_id IS NULL) OR (deleted_at IS NOT NULL AND deleted_by_account_id IS NOT NULL)");
                    table.CheckConstraint("ck_processing_executions_execution_mode", "execution_mode_snapshot IN ('SOURCE_TRACKED', 'POOLED_OUTPUT', 'FINAL_PACKAGING')");
                    table.CheckConstraint("ck_processing_executions_negative_inventory_policy", "NULLIF(btrim(negative_inventory_policy_snapshot), '') IS NOT NULL");
                    table.CheckConstraint("ck_processing_executions_row_version", "row_version >= 1");
                    table.CheckConstraint("ck_processing_executions_source_shape", "(execution_mode_snapshot = 'SOURCE_TRACKED' AND ((processing_source_kind = 'SUPPLIER' AND supplier_id IS NOT NULL) OR (processing_source_kind = 'FARMERS_COMBINED' AND supplier_id IS NULL))) OR (execution_mode_snapshot IN ('POOLED_OUTPUT', 'FINAL_PACKAGING') AND processing_source_kind IS NULL AND supplier_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_processing_executions_batch",
                        column: x => x.procurement_batch_id,
                        principalSchema: "procurement",
                        principalTable: "procurement_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_processing_executions_deleted_by_account",
                        column: x => x.deleted_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_processing_executions_employee",
                        column: x => x.employee_id,
                        principalSchema: "party",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_processing_executions_module_route_version",
                        columns: x => new { x.processing_module_id, x.processing_route_version_id },
                        principalSchema: "processing_config",
                        principalTable: "processing_modules",
                        principalColumns: new[] { "id", "processing_route_version_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_processing_executions_recorded_by_account",
                        column: x => x.recorded_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_processing_executions_route_version",
                        column: x => x.processing_route_version_id,
                        principalSchema: "processing_config",
                        principalTable: "processing_route_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_processing_executions_supplier",
                        column: x => x.supplier_id,
                        principalSchema: "party",
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "processing_module_outputs",
                schema: "processing_config",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    processing_module_id = table.Column<Guid>(type: "uuid", nullable: false),
                    processing_route_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    output_sequence = table.Column<int>(type: "integer", nullable: false),
                    output_kind = table.Column<string>(type: "text", nullable: false),
                    process_material_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sales_product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    default_wage_rate = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processing_module_outputs", x => x.id);
                    table.CheckConstraint("ck_processing_module_outputs_default_wage_rate", "default_wage_rate NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND default_wage_rate >= 0");
                    table.CheckConstraint("ck_processing_module_outputs_kind", "output_kind IN ('PROCESS_MATERIAL', 'SALES_PRODUCT')");
                    table.CheckConstraint("ck_processing_module_outputs_sequence", "output_sequence > 0");
                    table.CheckConstraint("ck_processing_module_outputs_typed_output", "(output_kind = 'PROCESS_MATERIAL' AND process_material_id IS NOT NULL AND sales_product_id IS NULL) OR (output_kind = 'SALES_PRODUCT' AND process_material_id IS NULL AND sales_product_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_processing_module_outputs_material_route_version",
                        columns: x => new { x.process_material_id, x.processing_route_version_id },
                        principalSchema: "processing_config",
                        principalTable: "process_materials",
                        principalColumns: new[] { "id", "processing_route_version_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_processing_module_outputs_module_route_version",
                        columns: x => new { x.processing_module_id, x.processing_route_version_id },
                        principalSchema: "processing_config",
                        principalTable: "processing_modules",
                        principalColumns: new[] { "id", "processing_route_version_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_processing_module_outputs_sales_product",
                        column: x => x.sales_product_id,
                        principalSchema: "product",
                        principalTable: "sales_products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payable_adjustments",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    adjustment_type = table.Column<string>(type: "text", nullable: false),
                    amount_delta_thb = table.Column<long>(type: "bigint", nullable: false),
                    reason_text = table.Column<string>(type: "text", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_account_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payable_adjustments", x => x.id);
                    table.CheckConstraint("ck_payable_adjustments_amount_delta", "amount_delta_thb < 0");
                    table.CheckConstraint("ck_payable_adjustments_type", "adjustment_type = 'SUPPLIER_QUALITY_WEIGHT_DEDUCTION'");
                    table.ForeignKey(
                        name: "fk_payable_adjustments_payable",
                        column: x => x.payable_id,
                        principalSchema: "finance",
                        principalTable: "payables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payable_adjustments_recorded_by_account",
                        column: x => x.recorded_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payable_outstanding_positions",
                schema: "finance",
                columns: table => new
                {
                    payable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_obligation_thb = table.Column<long>(type: "bigint", nullable: false),
                    adjustment_total_thb = table.Column<long>(type: "bigint", nullable: false),
                    settlement_total_thb = table.Column<long>(type: "bigint", nullable: false),
                    outstanding_thb = table.Column<long>(type: "bigint", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payable_outstanding_positions", x => x.payable_id);
                    table.CheckConstraint("ck_payable_outstanding_positions_formula", "outstanding_thb = original_obligation_thb + adjustment_total_thb - settlement_total_thb");
                    table.CheckConstraint("ck_payable_outstanding_positions_original", "original_obligation_thb >= 0");
                    table.CheckConstraint("ck_payable_outstanding_positions_row_version", "row_version >= 1");
                    table.CheckConstraint("ck_payable_outstanding_positions_settlement", "settlement_total_thb >= 0");
                    table.ForeignKey(
                        name: "fk_payable_outstanding_positions_payable",
                        column: x => x.payable_id,
                        principalSchema: "finance",
                        principalTable: "payables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_thb = table.Column<long>(type: "bigint", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_by_account_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.CheckConstraint("ck_payments_amount", "amount_thb > 0");
                    table.ForeignKey(
                        name: "fk_payments_confirmed_by_account",
                        column: x => x.confirmed_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payments_payable",
                        column: x => x.payable_id,
                        principalSchema: "finance",
                        principalTable: "payables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "company_pickup_transport_bases",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    procurement_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    applicable_quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_pickup_transport_bases", x => x.id);
                    table.CheckConstraint("ck_company_pickup_transport_bases_quantity", "applicable_quantity NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND applicable_quantity >= 0");
                    table.ForeignKey(
                        name: "fk_company_pickup_transport_bases_procurement_entry",
                        column: x => x.procurement_entry_id,
                        principalSchema: "procurement",
                        principalTable: "procurement_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payable_obligation_items",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payable_kind = table.Column<string>(type: "text", nullable: false),
                    obligation_kind = table.Column<string>(type: "text", nullable: false),
                    amount_thb = table.Column<long>(type: "bigint", nullable: false),
                    procurement_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    outsourced_supply_detail_id = table.Column<Guid>(type: "uuid", nullable: true),
                    employee_daily_wage_id = table.Column<Guid>(type: "uuid", nullable: true),
                    procurement_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    aggregated_applicable_quantity = table.Column<decimal>(type: "numeric", nullable: true),
                    applied_rate_per_kg = table.Column<decimal>(type: "numeric", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payable_obligation_items", x => x.id);
                    table.CheckConstraint("ck_payable_obligation_items_amount", "amount_thb >= 0");
                    table.CheckConstraint("ck_payable_obligation_items_kind", "obligation_kind IN ('PROCUREMENT_ENTRY', 'OUTSOURCED_SUPPLY_DETAIL', 'EMPLOYEE_DAILY_WAGE', 'COMPANY_PICKUP_TRANSPORT')");
                    table.CheckConstraint("ck_payable_obligation_items_transport_quantity", "aggregated_applicable_quantity IS NULL OR (aggregated_applicable_quantity NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND aggregated_applicable_quantity >= 0)");
                    table.CheckConstraint("ck_payable_obligation_items_transport_rate", "applied_rate_per_kg IS NULL OR (applied_rate_per_kg NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND applied_rate_per_kg >= 0)");
                    table.CheckConstraint("ck_payable_obligation_items_typed_source", "(obligation_kind = 'PROCUREMENT_ENTRY' AND payable_kind IN ('PROCUREMENT_SUPPLIER', 'PROCUREMENT_FARMER') AND procurement_entry_id IS NOT NULL AND outsourced_supply_detail_id IS NULL AND employee_daily_wage_id IS NULL AND procurement_batch_id IS NULL AND aggregated_applicable_quantity IS NULL AND applied_rate_per_kg IS NULL) OR (obligation_kind = 'OUTSOURCED_SUPPLY_DETAIL' AND payable_kind = 'OUTSOURCED_VENDOR' AND procurement_entry_id IS NULL AND outsourced_supply_detail_id IS NOT NULL AND employee_daily_wage_id IS NULL AND procurement_batch_id IS NULL AND aggregated_applicable_quantity IS NULL AND applied_rate_per_kg IS NULL) OR (obligation_kind = 'EMPLOYEE_DAILY_WAGE' AND payable_kind = 'EMPLOYEE_DAILY_WAGE' AND procurement_entry_id IS NULL AND outsourced_supply_detail_id IS NULL AND employee_daily_wage_id IS NOT NULL AND procurement_batch_id IS NULL AND aggregated_applicable_quantity IS NULL AND applied_rate_per_kg IS NULL) OR (obligation_kind = 'COMPANY_PICKUP_TRANSPORT' AND payable_kind = 'COMPANY_PICKUP_TRANSPORT' AND procurement_entry_id IS NULL AND outsourced_supply_detail_id IS NULL AND employee_daily_wage_id IS NULL AND procurement_batch_id IS NOT NULL AND aggregated_applicable_quantity IS NOT NULL AND applied_rate_per_kg IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_payable_obligation_items_employee_daily_wage",
                        column: x => x.employee_daily_wage_id,
                        principalSchema: "labor",
                        principalTable: "employee_daily_wages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payable_obligation_items_outsourced_supply_detail",
                        column: x => x.outsourced_supply_detail_id,
                        principalSchema: "outsourced",
                        principalTable: "outsourced_supply_details",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payable_obligation_items_payable_kind",
                        columns: x => new { x.payable_id, x.payable_kind },
                        principalSchema: "finance",
                        principalTable: "payables",
                        principalColumns: new[] { "id", "payable_kind" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payable_obligation_items_procurement_batch",
                        column: x => x.procurement_batch_id,
                        principalSchema: "procurement",
                        principalTable: "procurement_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payable_obligation_items_procurement_entry",
                        column: x => x.procurement_entry_id,
                        principalSchema: "procurement",
                        principalTable: "procurement_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_allocations",
                schema: "sales",
                columns: table => new
                {
                    sales_detail_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    sales_allocation_revision_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_allocations", x => new { x.sales_detail_id, x.sequence });
                    table.CheckConstraint("ck_sales_allocations_row_version", "row_version >= 1");
                    table.CheckConstraint("ck_sales_allocations_sequence", "sequence > 0");
                    table.ForeignKey(
                        name: "fk_sales_allocations_revision_item_detail",
                        columns: x => new { x.sales_allocation_revision_item_id, x.sales_detail_id },
                        principalSchema: "sales",
                        principalTable: "sales_allocation_revision_items",
                        principalColumns: new[] { "id", "sales_detail_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_allocations_sales_detail",
                        column: x => x.sales_detail_id,
                        principalSchema: "sales",
                        principalTable: "sales_details",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_operations",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_type = table.Column<string>(type: "text", nullable: false),
                    procurement_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    processing_execution_id = table.Column<Guid>(type: "uuid", nullable: true),
                    outsourced_supply_detail_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sales_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sales_allocation_revision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    procurement_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_account_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_operations", x => x.id);
                    table.CheckConstraint("ck_inventory_operations_source_shape", "(operation_type = 'PROCUREMENT_RECEIPT' AND procurement_entry_id IS NOT NULL AND processing_execution_id IS NULL AND outsourced_supply_detail_id IS NULL AND sales_id IS NULL AND sales_allocation_revision_id IS NULL AND procurement_batch_id IS NULL) OR (operation_type = 'PROCESSING' AND procurement_entry_id IS NULL AND processing_execution_id IS NOT NULL AND outsourced_supply_detail_id IS NULL AND sales_id IS NULL AND sales_allocation_revision_id IS NULL AND procurement_batch_id IS NULL) OR (operation_type = 'OUTSOURCED_RECEIPT' AND procurement_entry_id IS NULL AND processing_execution_id IS NULL AND outsourced_supply_detail_id IS NOT NULL AND sales_id IS NULL AND sales_allocation_revision_id IS NULL AND procurement_batch_id IS NULL) OR (operation_type = 'SALES_ISSUE' AND procurement_entry_id IS NULL AND processing_execution_id IS NULL AND outsourced_supply_detail_id IS NULL AND sales_id IS NOT NULL AND sales_allocation_revision_id IS NULL AND procurement_batch_id IS NULL) OR (operation_type = 'SALES_ALLOCATION_REVISION' AND procurement_entry_id IS NULL AND processing_execution_id IS NULL AND outsourced_supply_detail_id IS NULL AND sales_id IS NULL AND sales_allocation_revision_id IS NOT NULL AND procurement_batch_id IS NULL) OR (operation_type = 'BATCH_RECONCILIATION' AND procurement_entry_id IS NULL AND processing_execution_id IS NULL AND outsourced_supply_detail_id IS NULL AND sales_id IS NULL AND sales_allocation_revision_id IS NULL AND procurement_batch_id IS NOT NULL) OR (operation_type IN ('TRANSFER', 'ADJUSTMENT') AND procurement_entry_id IS NULL AND processing_execution_id IS NULL AND outsourced_supply_detail_id IS NULL AND sales_id IS NULL AND sales_allocation_revision_id IS NULL AND procurement_batch_id IS NULL)");
                    table.CheckConstraint("ck_inventory_operations_type", "operation_type IN ('PROCUREMENT_RECEIPT', 'PROCESSING', 'TRANSFER', 'ADJUSTMENT', 'BATCH_RECONCILIATION', 'OUTSOURCED_RECEIPT', 'SALES_ISSUE', 'SALES_ALLOCATION_REVISION')");
                    table.ForeignKey(
                        name: "fk_inventory_operations_outsourced_supply_detail",
                        column: x => x.outsourced_supply_detail_id,
                        principalSchema: "outsourced",
                        principalTable: "outsourced_supply_details",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_operations_processing_execution",
                        column: x => x.processing_execution_id,
                        principalSchema: "processing",
                        principalTable: "processing_executions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_operations_procurement_batch",
                        column: x => x.procurement_batch_id,
                        principalSchema: "procurement",
                        principalTable: "procurement_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_operations_procurement_entry",
                        column: x => x.procurement_entry_id,
                        principalSchema: "procurement",
                        principalTable: "procurement_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_operations_recorded_by_account",
                        column: x => x.recorded_by_account_id,
                        principalSchema: "system",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_operations_sales",
                        column: x => x.sales_id,
                        principalSchema: "sales",
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_operations_sales_allocation_revision",
                        column: x => x.sales_allocation_revision_id,
                        principalSchema: "sales",
                        principalTable: "sales_allocation_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "processing_execution_inputs",
                schema: "processing",
                columns: table => new
                {
                    processing_execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consumption_basis = table.Column<string>(type: "text", nullable: false),
                    consumed_quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    observed_scale_reading = table.Column<decimal>(type: "numeric", nullable: true),
                    actual_container_count = table.Column<int>(type: "integer", nullable: true),
                    tare_weight_snapshot = table.Column<decimal>(type: "numeric", nullable: true),
                    derived_net_quantity = table.Column<decimal>(type: "numeric", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processing_execution_inputs", x => x.processing_execution_id);
                    table.CheckConstraint("ck_processing_execution_inputs_consumed_quantity", "consumed_quantity NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND consumed_quantity >= 0 AND consumed_quantity = round(consumed_quantity, 1)");
                    table.CheckConstraint("ck_processing_execution_inputs_consumption_basis", "consumption_basis IN ('SCALE_NET', 'OUTPUT_QUANTITY', 'PACKAGING_WEIGHT')");
                    table.CheckConstraint("ck_processing_execution_inputs_container_count", "actual_container_count IS NULL OR actual_container_count >= 0");
                    table.CheckConstraint("ck_processing_execution_inputs_derived_net_quantity", "derived_net_quantity IS NULL OR (derived_net_quantity NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND derived_net_quantity >= 0 AND derived_net_quantity = round(derived_net_quantity, 1))");
                    table.CheckConstraint("ck_processing_execution_inputs_scale_reading", "observed_scale_reading IS NULL OR (observed_scale_reading NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND observed_scale_reading >= 0 AND observed_scale_reading = round(observed_scale_reading, 1))");
                    table.CheckConstraint("ck_processing_execution_inputs_shape", "(consumption_basis = 'SCALE_NET' AND observed_scale_reading IS NOT NULL AND derived_net_quantity IS NOT NULL AND consumed_quantity = derived_net_quantity AND (((actual_container_count IS NULL AND tare_weight_snapshot IS NULL) AND derived_net_quantity = observed_scale_reading) OR (actual_container_count IS NOT NULL AND tare_weight_snapshot IS NOT NULL AND derived_net_quantity = observed_scale_reading - (actual_container_count * tare_weight_snapshot)))) OR (consumption_basis IN ('OUTPUT_QUANTITY', 'PACKAGING_WEIGHT') AND observed_scale_reading IS NULL AND actual_container_count IS NULL AND tare_weight_snapshot IS NULL AND derived_net_quantity IS NULL)");
                    table.CheckConstraint("ck_processing_execution_inputs_tare_snapshot", "tare_weight_snapshot IS NULL OR (tare_weight_snapshot NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND tare_weight_snapshot >= 0)");
                    table.ForeignKey(
                        name: "fk_processing_execution_inputs_execution",
                        column: x => x.processing_execution_id,
                        principalSchema: "processing",
                        principalTable: "processing_executions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "processing_execution_outputs",
                schema: "processing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    processing_execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    processing_module_output_id = table.Column<Guid>(type: "uuid", nullable: false),
                    output_kind_snapshot = table.Column<string>(type: "text", nullable: false),
                    configured_wage_rate_snapshot = table.Column<decimal>(type: "numeric", nullable: false),
                    observed_scale_reading = table.Column<decimal>(type: "numeric", nullable: true),
                    actual_container_count = table.Column<int>(type: "integer", nullable: true),
                    tare_weight_snapshot = table.Column<decimal>(type: "numeric", nullable: true),
                    derived_net_quantity = table.Column<decimal>(type: "numeric", nullable: true),
                    completed_quantity = table.Column<decimal>(type: "numeric", nullable: true),
                    packaging_weight_snapshot = table.Column<decimal>(type: "numeric", nullable: true),
                    source_consumption_quantity = table.Column<decimal>(type: "numeric", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processing_execution_outputs", x => x.id);
                    table.CheckConstraint("ck_processing_execution_outputs_completed_quantity", "completed_quantity IS NULL OR (completed_quantity NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND completed_quantity >= 0 AND completed_quantity = round(completed_quantity, 1))");
                    table.CheckConstraint("ck_processing_execution_outputs_container_count", "actual_container_count IS NULL OR actual_container_count >= 0");
                    table.CheckConstraint("ck_processing_execution_outputs_derived_net_quantity", "derived_net_quantity IS NULL OR (derived_net_quantity NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND derived_net_quantity >= 0 AND derived_net_quantity = round(derived_net_quantity, 1))");
                    table.CheckConstraint("ck_processing_execution_outputs_final_packaging_formula", "output_kind_snapshot <> 'SALES_PRODUCT' OR source_consumption_quantity = completed_quantity * packaging_weight_snapshot");
                    table.CheckConstraint("ck_processing_execution_outputs_kind", "output_kind_snapshot IN ('PROCESS_MATERIAL', 'SALES_PRODUCT')");
                    table.CheckConstraint("ck_processing_execution_outputs_packaging_weight", "packaging_weight_snapshot IS NULL OR (packaging_weight_snapshot NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND packaging_weight_snapshot > 0)");
                    table.CheckConstraint("ck_processing_execution_outputs_scale_reading", "observed_scale_reading IS NULL OR (observed_scale_reading NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND observed_scale_reading >= 0 AND observed_scale_reading = round(observed_scale_reading, 1))");
                    table.CheckConstraint("ck_processing_execution_outputs_shape", "(output_kind_snapshot = 'PROCESS_MATERIAL' AND observed_scale_reading IS NOT NULL AND derived_net_quantity IS NOT NULL AND completed_quantity IS NULL AND packaging_weight_snapshot IS NULL AND source_consumption_quantity IS NULL AND (((actual_container_count IS NULL AND tare_weight_snapshot IS NULL) AND derived_net_quantity = observed_scale_reading) OR (actual_container_count IS NOT NULL AND tare_weight_snapshot IS NOT NULL AND derived_net_quantity = observed_scale_reading - (actual_container_count * tare_weight_snapshot)))) OR (output_kind_snapshot = 'SALES_PRODUCT' AND observed_scale_reading IS NULL AND actual_container_count IS NULL AND tare_weight_snapshot IS NULL AND derived_net_quantity IS NULL AND completed_quantity IS NOT NULL AND packaging_weight_snapshot IS NOT NULL AND source_consumption_quantity IS NOT NULL)");
                    table.CheckConstraint("ck_processing_execution_outputs_source_consumption", "source_consumption_quantity IS NULL OR (source_consumption_quantity NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND source_consumption_quantity >= 0)");
                    table.CheckConstraint("ck_processing_execution_outputs_tare_snapshot", "tare_weight_snapshot IS NULL OR (tare_weight_snapshot NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND tare_weight_snapshot >= 0)");
                    table.CheckConstraint("ck_processing_execution_outputs_wage_rate", "configured_wage_rate_snapshot NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND configured_wage_rate_snapshot >= 0");
                    table.ForeignKey(
                        name: "fk_processing_execution_outputs_execution",
                        column: x => x.processing_execution_id,
                        principalSchema: "processing",
                        principalTable: "processing_executions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_processing_execution_outputs_module_output",
                        column: x => x.processing_module_output_id,
                        principalSchema: "processing_config",
                        principalTable: "processing_module_outputs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "processing_wage_components",
                schema: "labor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_daily_wage_id = table.Column<Guid>(type: "uuid", nullable: false),
                    processing_module_output_id = table.Column<Guid>(type: "uuid", nullable: false),
                    configured_wage_rate_snapshot = table.Column<decimal>(type: "numeric", nullable: false),
                    applied_wage_rate = table.Column<decimal>(type: "numeric", nullable: false),
                    rate_overridden = table.Column<bool>(type: "boolean", nullable: false),
                    aggregated_quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    amount_thb = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processing_wage_components", x => x.id);
                    table.CheckConstraint("ck_processing_wage_components_aggregated_quantity", "aggregated_quantity NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND aggregated_quantity >= 0");
                    table.CheckConstraint("ck_processing_wage_components_amount", "amount_thb >= 0");
                    table.CheckConstraint("ck_processing_wage_components_amount_formula", "amount_thb = floor(aggregated_quantity * applied_wage_rate)");
                    table.CheckConstraint("ck_processing_wage_components_applied_rate", "applied_wage_rate NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND applied_wage_rate >= 0");
                    table.CheckConstraint("ck_processing_wage_components_configured_rate", "configured_wage_rate_snapshot NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND configured_wage_rate_snapshot >= 0");
                    table.ForeignKey(
                        name: "fk_processing_wage_components_daily_wage",
                        column: x => x.employee_daily_wage_id,
                        principalSchema: "labor",
                        principalTable: "employee_daily_wages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_processing_wage_components_module_output",
                        column: x => x.processing_module_output_id,
                        principalSchema: "processing_config",
                        principalTable: "processing_module_outputs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "company_pickup_transport_obligation_basis_items",
                schema: "finance",
                columns: table => new
                {
                    transport_obligation_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transport_basis_id = table.Column<Guid>(type: "uuid", nullable: false),
                    applied_quantity = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_pickup_transport_obligation_basis_items", x => new { x.transport_obligation_item_id, x.transport_basis_id });
                    table.CheckConstraint("ck_company_pickup_transport_obligation_basis_items_quantity", "applied_quantity NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND applied_quantity >= 0");
                    table.ForeignKey(
                        name: "fk_company_pickup_transport_obligation_basis_items_basis",
                        column: x => x.transport_basis_id,
                        principalSchema: "finance",
                        principalTable: "company_pickup_transport_bases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_company_pickup_transport_obligation_basis_items_obligation",
                        column: x => x.transport_obligation_item_id,
                        principalSchema: "finance",
                        principalTable: "payable_obligation_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_movements",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    inventory_operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    movement_type = table.Column<string>(type: "text", nullable: false),
                    origin = table.Column<string>(type: "text", nullable: false),
                    procurement_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    outsourced_supply_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inventory_object_kind = table.Column<string>(type: "text", nullable: false),
                    procurement_product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    process_material_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sales_product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    storage_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    raw_source_kind = table.Column<string>(type: "text", nullable: true),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity_delta = table.Column<decimal>(type: "numeric", nullable: false),
                    sales_allocation_revision_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_movements", x => x.id);
                    table.CheckConstraint("ck_inventory_movements_allocation_lineage", "(movement_type IN ('SALES_ISSUE', 'SALES_ALLOCATION_ADJUSTMENT') AND sales_allocation_revision_item_id IS NOT NULL) OR (movement_type NOT IN ('SALES_ISSUE', 'SALES_ALLOCATION_ADJUSTMENT') AND sales_allocation_revision_item_id IS NULL)");
                    table.CheckConstraint("ck_inventory_movements_object_kind", "inventory_object_kind IN ('PROCUREMENT_PRODUCT', 'PROCESS_MATERIAL', 'SALES_PRODUCT')");
                    table.CheckConstraint("ck_inventory_movements_origin", "origin IN ('IN_HOUSE', 'OUTSOURCED')");
                    table.CheckConstraint("ck_inventory_movements_quantity_sign", "quantity_delta NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND ((movement_type IN ('PURCHASE_RECEIPT', 'PROCESS_PRODUCE', 'FINAL_PACKAGE_PRODUCE', 'OUTSOURCED_RECEIPT', 'TRANSFER_IN') AND quantity_delta > 0) OR (movement_type IN ('PROCESS_CONSUME', 'FINAL_PACKAGE_CONSUME', 'TRANSFER_OUT', 'SALES_ISSUE') AND quantity_delta < 0) OR (movement_type IN ('SALES_ALLOCATION_ADJUSTMENT', 'ADJUSTMENT', 'BATCH_RECONCILIATION') AND quantity_delta <> 0))");
                    table.CheckConstraint("ck_inventory_movements_raw_source_kind", "raw_source_kind IS NULL OR raw_source_kind IN ('SUPPLIER', 'FARMERS_COMBINED')");
                    table.CheckConstraint("ck_inventory_movements_raw_source_shape", "(raw_source_kind IS NULL AND supplier_id IS NULL) OR (origin = 'IN_HOUSE' AND inventory_object_kind = 'PROCUREMENT_PRODUCT' AND raw_source_kind = 'SUPPLIER' AND supplier_id IS NOT NULL) OR (origin = 'IN_HOUSE' AND inventory_object_kind = 'PROCUREMENT_PRODUCT' AND raw_source_kind = 'FARMERS_COMBINED' AND supplier_id IS NULL)");
                    table.CheckConstraint("ck_inventory_movements_sequence", "sequence > 0");
                    table.CheckConstraint("ck_inventory_movements_source_batch", "(origin = 'IN_HOUSE' AND procurement_batch_id IS NOT NULL AND outsourced_supply_batch_id IS NULL) OR (origin = 'OUTSOURCED' AND procurement_batch_id IS NULL AND outsourced_supply_batch_id IS NOT NULL)");
                    table.CheckConstraint("ck_inventory_movements_type", "movement_type IN ('PURCHASE_RECEIPT', 'PROCESS_CONSUME', 'PROCESS_PRODUCE', 'FINAL_PACKAGE_CONSUME', 'FINAL_PACKAGE_PRODUCE', 'OUTSOURCED_RECEIPT', 'TRANSFER_OUT', 'TRANSFER_IN', 'SALES_ISSUE', 'SALES_ALLOCATION_ADJUSTMENT', 'ADJUSTMENT', 'BATCH_RECONCILIATION')");
                    table.CheckConstraint("ck_inventory_movements_typed_object", "(inventory_object_kind = 'PROCUREMENT_PRODUCT' AND procurement_product_id IS NOT NULL AND process_material_id IS NULL AND sales_product_id IS NULL) OR (inventory_object_kind = 'PROCESS_MATERIAL' AND procurement_product_id IS NULL AND process_material_id IS NOT NULL AND sales_product_id IS NULL) OR (inventory_object_kind = 'SALES_PRODUCT' AND procurement_product_id IS NULL AND process_material_id IS NULL AND sales_product_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_inventory_movements_operation",
                        column: x => x.inventory_operation_id,
                        principalSchema: "inventory",
                        principalTable: "inventory_operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_movements_outsourced_batch",
                        column: x => x.outsourced_supply_batch_id,
                        principalSchema: "outsourced",
                        principalTable: "outsourced_supply_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_movements_process_material",
                        column: x => x.process_material_id,
                        principalSchema: "processing_config",
                        principalTable: "process_materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_movements_procurement_batch",
                        column: x => x.procurement_batch_id,
                        principalSchema: "procurement",
                        principalTable: "procurement_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_movements_procurement_product",
                        column: x => x.procurement_product_id,
                        principalSchema: "product",
                        principalTable: "procurement_products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_movements_sales_allocation_revision_item",
                        column: x => x.sales_allocation_revision_item_id,
                        principalSchema: "sales",
                        principalTable: "sales_allocation_revision_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_movements_sales_product",
                        column: x => x.sales_product_id,
                        principalSchema: "product",
                        principalTable: "sales_products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_movements_storage_location",
                        column: x => x.storage_location_id,
                        principalSchema: "infrastructure",
                        principalTable: "storage_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_movements_supplier",
                        column: x => x.supplier_id,
                        principalSchema: "party",
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "processing_wage_component_sources",
                schema: "labor",
                columns: table => new
                {
                    processing_wage_component_id = table.Column<Guid>(type: "uuid", nullable: false),
                    processing_execution_output_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity_snapshot = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processing_wage_component_sources", x => new { x.processing_wage_component_id, x.processing_execution_output_id });
                    table.ForeignKey(
                        name: "fk_processing_wage_component_sources_component",
                        column: x => x.processing_wage_component_id,
                        principalSchema: "labor",
                        principalTable: "processing_wage_components",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_processing_wage_component_sources_execution_output",
                        column: x => x.processing_execution_output_id,
                        principalSchema: "processing",
                        principalTable: "processing_execution_outputs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_accounts_identity_issuer_subject",
                schema: "system",
                table: "accounts",
                columns: new[] { "identity_issuer", "identity_subject" },
                unique: true,
                filter: "identity_issuer IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_actor_occurred_at",
                schema: "audit",
                table: "audit_events",
                columns: new[] { "actor_account_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_command_id",
                schema: "audit",
                table: "audit_events",
                column: "command_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_kind_occurred_at",
                schema: "audit",
                table: "audit_events",
                columns: new[] { "event_kind", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_command_executions_actor_account_id",
                schema: "system",
                table: "command_executions",
                column: "actor_account_id");

            migrationBuilder.CreateIndex(
                name: "ux_company_pickup_transport_bases_procurement_entry",
                schema: "finance",
                table: "company_pickup_transport_bases",
                column: "procurement_entry_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_company_pickup_transport_obligation_basis_items_basis_id",
                schema: "finance",
                table: "company_pickup_transport_obligation_basis_items",
                column: "transport_basis_id");

            migrationBuilder.CreateIndex(
                name: "ix_containers_created_by_account_id",
                schema: "infrastructure",
                table: "containers",
                column: "created_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_containers_deleted_by_account_id",
                schema: "infrastructure",
                table: "containers",
                column: "deleted_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_correction_links_corrected_event_id",
                schema: "audit",
                table: "correction_links",
                column: "corrected_audit_event_id");

            migrationBuilder.CreateIndex(
                name: "ix_customers_created_by_account_id",
                schema: "party",
                table: "customers",
                column: "created_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_customers_deleted_by_account_id",
                schema: "party",
                table: "customers",
                column: "deleted_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_daily_wages_confirmed_by_account_id",
                schema: "labor",
                table: "employee_daily_wages",
                column: "confirmed_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_daily_wages_employee_id",
                schema: "labor",
                table: "employee_daily_wages",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ux_employee_daily_wages_work_date_employee",
                schema: "labor",
                table: "employee_daily_wages",
                columns: new[] { "work_date", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employees_created_by_account_id",
                schema: "party",
                table: "employees",
                column: "created_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_employees_deleted_by_account_id",
                schema: "party",
                table: "employees",
                column: "deleted_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_farmers_created_by_account_id",
                schema: "party",
                table: "farmers",
                column: "created_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_farmers_deleted_by_account_id",
                schema: "party",
                table: "farmers",
                column: "deleted_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movements_outsourced_batch_id",
                schema: "inventory",
                table: "inventory_movements",
                column: "outsourced_supply_batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movements_process_material_id",
                schema: "inventory",
                table: "inventory_movements",
                column: "process_material_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movements_procurement_batch_id",
                schema: "inventory",
                table: "inventory_movements",
                column: "procurement_batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movements_procurement_product_id",
                schema: "inventory",
                table: "inventory_movements",
                column: "procurement_product_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movements_sales_allocation_revision_item_id",
                schema: "inventory",
                table: "inventory_movements",
                column: "sales_allocation_revision_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movements_sales_product_id",
                schema: "inventory",
                table: "inventory_movements",
                column: "sales_product_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movements_storage_location_id",
                schema: "inventory",
                table: "inventory_movements",
                column: "storage_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movements_supplier_id",
                schema: "inventory",
                table: "inventory_movements",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ux_inventory_movements_operation_sequence",
                schema: "inventory",
                table: "inventory_movements",
                columns: new[] { "inventory_operation_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_operations_procurement_batch_id",
                schema: "inventory",
                table: "inventory_operations",
                column: "procurement_batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_operations_recorded_by_account_id",
                schema: "inventory",
                table: "inventory_operations",
                column: "recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ux_inventory_operations_outsourced_receipt_source",
                schema: "inventory",
                table: "inventory_operations",
                column: "outsourced_supply_detail_id",
                unique: true,
                filter: "operation_type = 'OUTSOURCED_RECEIPT'");

            migrationBuilder.CreateIndex(
                name: "ux_inventory_operations_processing_source",
                schema: "inventory",
                table: "inventory_operations",
                column: "processing_execution_id",
                unique: true,
                filter: "operation_type = 'PROCESSING'");

            migrationBuilder.CreateIndex(
                name: "ux_inventory_operations_procurement_receipt_source",
                schema: "inventory",
                table: "inventory_operations",
                column: "procurement_entry_id",
                unique: true,
                filter: "operation_type = 'PROCUREMENT_RECEIPT'");

            migrationBuilder.CreateIndex(
                name: "ux_inventory_operations_sales_allocation_revision_source",
                schema: "inventory",
                table: "inventory_operations",
                column: "sales_allocation_revision_id",
                unique: true,
                filter: "operation_type = 'SALES_ALLOCATION_REVISION'");

            migrationBuilder.CreateIndex(
                name: "ux_inventory_operations_sales_issue_source",
                schema: "inventory",
                table: "inventory_operations",
                column: "sales_id",
                unique: true,
                filter: "operation_type = 'SALES_ISSUE'");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_positions_outsourced_batch_id",
                schema: "inventory",
                table: "inventory_positions",
                column: "outsourced_supply_batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_positions_process_material_id",
                schema: "inventory",
                table: "inventory_positions",
                column: "process_material_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_positions_procurement_batch_id",
                schema: "inventory",
                table: "inventory_positions",
                column: "procurement_batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_positions_procurement_product_id",
                schema: "inventory",
                table: "inventory_positions",
                column: "procurement_product_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_positions_sales_product_id",
                schema: "inventory",
                table: "inventory_positions",
                column: "sales_product_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_positions_storage_location_id",
                schema: "inventory",
                table: "inventory_positions",
                column: "storage_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_positions_supplier_id",
                schema: "inventory",
                table: "inventory_positions",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ux_inventory_positions_full_identity",
                schema: "inventory",
                table: "inventory_positions",
                columns: new[] { "origin", "procurement_batch_id", "outsourced_supply_batch_id", "inventory_object_kind", "procurement_product_id", "process_material_id", "sales_product_id", "storage_location_id", "raw_source_kind", "supplier_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_outsourced_supply_batches_closed_by_account_id",
                schema: "outsourced",
                table: "outsourced_supply_batches",
                column: "closed_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_outsourced_supply_batches_created_by_account_id",
                schema: "outsourced",
                table: "outsourced_supply_batches",
                column: "created_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_outsourced_supply_batches_deleted_by_account_id",
                schema: "outsourced",
                table: "outsourced_supply_batches",
                column: "deleted_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_outsourced_supply_batches_vendor_id",
                schema: "outsourced",
                table: "outsourced_supply_batches",
                column: "outsourced_vendor_id");

            migrationBuilder.CreateIndex(
                name: "ux_outsourced_supply_batches_date_vendor",
                schema: "outsourced",
                table: "outsourced_supply_batches",
                columns: new[] { "supply_date", "outsourced_vendor_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outsourced_supply_details_batch_id",
                schema: "outsourced",
                table: "outsourced_supply_details",
                column: "outsourced_supply_batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_outsourced_supply_details_deleted_by_account_id",
                schema: "outsourced",
                table: "outsourced_supply_details",
                column: "deleted_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_outsourced_supply_details_recorded_by_account_id",
                schema: "outsourced",
                table: "outsourced_supply_details",
                column: "recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_outsourced_supply_details_sales_product_id",
                schema: "outsourced",
                table: "outsourced_supply_details",
                column: "sales_product_id");

            migrationBuilder.CreateIndex(
                name: "ix_outsourced_vendors_created_by_account_id",
                schema: "party",
                table: "outsourced_vendors",
                column: "created_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_outsourced_vendors_deleted_by_account_id",
                schema: "party",
                table: "outsourced_vendors",
                column: "deleted_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_payable_adjustments_payable_id",
                schema: "finance",
                table: "payable_adjustments",
                column: "payable_id");

            migrationBuilder.CreateIndex(
                name: "ix_payable_adjustments_recorded_by_account_id",
                schema: "finance",
                table: "payable_adjustments",
                column: "recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_payable_obligation_items_payable_kind",
                schema: "finance",
                table: "payable_obligation_items",
                columns: new[] { "payable_id", "payable_kind" });

            migrationBuilder.CreateIndex(
                name: "ix_payable_obligation_items_procurement_batch_id",
                schema: "finance",
                table: "payable_obligation_items",
                column: "procurement_batch_id");

            migrationBuilder.CreateIndex(
                name: "ux_payable_obligation_items_employee_daily_wage",
                schema: "finance",
                table: "payable_obligation_items",
                column: "employee_daily_wage_id",
                unique: true,
                filter: "obligation_kind = 'EMPLOYEE_DAILY_WAGE'");

            migrationBuilder.CreateIndex(
                name: "ux_payable_obligation_items_outsourced_supply_detail",
                schema: "finance",
                table: "payable_obligation_items",
                column: "outsourced_supply_detail_id",
                unique: true,
                filter: "obligation_kind = 'OUTSOURCED_SUPPLY_DETAIL'");

            migrationBuilder.CreateIndex(
                name: "ux_payable_obligation_items_procurement_entry",
                schema: "finance",
                table: "payable_obligation_items",
                column: "procurement_entry_id",
                unique: true,
                filter: "obligation_kind = 'PROCUREMENT_ENTRY'");

            migrationBuilder.CreateIndex(
                name: "IX_payables_farmer_id",
                schema: "finance",
                table: "payables",
                column: "farmer_id");

            migrationBuilder.CreateIndex(
                name: "IX_payables_supplier_id",
                schema: "finance",
                table: "payables",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ux_payables_employee_daily_wage",
                schema: "finance",
                table: "payables",
                column: "employee_daily_wage_id",
                unique: true,
                filter: "payable_kind = 'EMPLOYEE_DAILY_WAGE'");

            migrationBuilder.CreateIndex(
                name: "ux_payables_outsourced_vendor",
                schema: "finance",
                table: "payables",
                column: "outsourced_supply_detail_id",
                unique: true,
                filter: "payable_kind = 'OUTSOURCED_VENDOR'");

            migrationBuilder.CreateIndex(
                name: "ux_payables_procurement_farmer",
                schema: "finance",
                table: "payables",
                columns: new[] { "procurement_batch_id", "farmer_id" },
                unique: true,
                filter: "payable_kind = 'PROCUREMENT_FARMER'");

            migrationBuilder.CreateIndex(
                name: "ux_payables_procurement_supplier",
                schema: "finance",
                table: "payables",
                columns: new[] { "procurement_batch_id", "supplier_id" },
                unique: true,
                filter: "payable_kind = 'PROCUREMENT_SUPPLIER'");

            migrationBuilder.CreateIndex(
                name: "ix_payments_confirmed_by_account_id",
                schema: "finance",
                table: "payments",
                column: "confirmed_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_payable_id",
                schema: "finance",
                table: "payments",
                column: "payable_id");

            migrationBuilder.CreateIndex(
                name: "ix_process_materials_container_id",
                schema: "processing_config",
                table: "process_materials",
                column: "container_id");

            migrationBuilder.CreateIndex(
                name: "ix_process_materials_created_by_account_id",
                schema: "processing_config",
                table: "process_materials",
                column: "created_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_process_materials_default_storage_location_id",
                schema: "processing_config",
                table: "process_materials",
                column: "default_storage_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_process_materials_deleted_by_account_id",
                schema: "processing_config",
                table: "process_materials",
                column: "deleted_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_process_materials_route_version_id",
                schema: "processing_config",
                table: "process_materials",
                column: "processing_route_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_processing_execution_outputs_module_output_id",
                schema: "processing",
                table: "processing_execution_outputs",
                column: "processing_module_output_id");

            migrationBuilder.CreateIndex(
                name: "ux_processing_execution_outputs_execution_definition",
                schema: "processing",
                table: "processing_execution_outputs",
                columns: new[] { "processing_execution_id", "processing_module_output_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_processing_execution_outputs_one_sales_product_per_execution",
                schema: "processing",
                table: "processing_execution_outputs",
                column: "processing_execution_id",
                unique: true,
                filter: "output_kind_snapshot = 'SALES_PRODUCT'");

            migrationBuilder.CreateIndex(
                name: "ix_processing_executions_batch_id",
                schema: "processing",
                table: "processing_executions",
                column: "procurement_batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_processing_executions_deleted_by_account_id",
                schema: "processing",
                table: "processing_executions",
                column: "deleted_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_processing_executions_employee_id",
                schema: "processing",
                table: "processing_executions",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_processing_executions_module_route_version",
                schema: "processing",
                table: "processing_executions",
                columns: new[] { "processing_module_id", "processing_route_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_processing_executions_recorded_by_account_id",
                schema: "processing",
                table: "processing_executions",
                column: "recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_processing_executions_route_version_id",
                schema: "processing",
                table: "processing_executions",
                column: "processing_route_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_processing_executions_supplier_id",
                schema: "processing",
                table: "processing_executions",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ix_processing_module_outputs_material_route_version",
                schema: "processing_config",
                table: "processing_module_outputs",
                columns: new[] { "process_material_id", "processing_route_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_processing_module_outputs_module_route_version",
                schema: "processing_config",
                table: "processing_module_outputs",
                columns: new[] { "processing_module_id", "processing_route_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_processing_module_outputs_route_version_id",
                schema: "processing_config",
                table: "processing_module_outputs",
                column: "processing_route_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_processing_module_outputs_sales_product_id",
                schema: "processing_config",
                table: "processing_module_outputs",
                column: "sales_product_id");

            migrationBuilder.CreateIndex(
                name: "ux_processing_module_outputs_module_sequence",
                schema: "processing_config",
                table: "processing_module_outputs",
                columns: new[] { "processing_module_id", "output_sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_processing_modules_input_material_route_version",
                schema: "processing_config",
                table: "processing_modules",
                columns: new[] { "input_process_material_id", "processing_route_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_processing_modules_route_version_id",
                schema: "processing_config",
                table: "processing_modules",
                column: "processing_route_version_id");

            migrationBuilder.CreateIndex(
                name: "ux_processing_route_versions_one_active_per_route",
                schema: "processing_config",
                table: "processing_route_versions",
                column: "processing_route_id",
                unique: true,
                filter: "status = 'ACTIVE'");

            migrationBuilder.CreateIndex(
                name: "ux_processing_route_versions_route_version_number",
                schema: "processing_config",
                table: "processing_route_versions",
                columns: new[] { "processing_route_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_processing_routes_created_by_account_id",
                schema: "processing_config",
                table: "processing_routes",
                column: "created_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_processing_routes_deleted_by_account_id",
                schema: "processing_config",
                table: "processing_routes",
                column: "deleted_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_processing_routes_procurement_product_id",
                schema: "processing_config",
                table: "processing_routes",
                column: "procurement_product_id");

            migrationBuilder.CreateIndex(
                name: "ux_processing_wage_component_sources_execution_output",
                schema: "labor",
                table: "processing_wage_component_sources",
                column: "processing_execution_output_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_processing_wage_components_daily_wage_id",
                schema: "labor",
                table: "processing_wage_components",
                column: "employee_daily_wage_id");

            migrationBuilder.CreateIndex(
                name: "ix_processing_wage_components_module_output_id",
                schema: "labor",
                table: "processing_wage_components",
                column: "processing_module_output_id");

            migrationBuilder.CreateIndex(
                name: "ix_procurement_batches_closed_by_account_id",
                schema: "procurement",
                table: "procurement_batches",
                column: "closed_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_procurement_batches_completed_by_account_id",
                schema: "procurement",
                table: "procurement_batches",
                column: "completed_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_procurement_batches_created_by_account_id",
                schema: "procurement",
                table: "procurement_batches",
                column: "created_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_procurement_batches_deleted_by_account_id",
                schema: "procurement",
                table: "procurement_batches",
                column: "deleted_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_procurement_batches_product_id",
                schema: "procurement",
                table: "procurement_batches",
                column: "procurement_product_id");

            migrationBuilder.CreateIndex(
                name: "ix_procurement_batches_route_id",
                schema: "procurement",
                table: "procurement_batches",
                column: "processing_route_id");

            migrationBuilder.CreateIndex(
                name: "ix_procurement_batches_route_version_route",
                schema: "procurement",
                table: "procurement_batches",
                columns: new[] { "processing_route_version_id", "processing_route_id" });

            migrationBuilder.CreateIndex(
                name: "ux_procurement_batches_date_product",
                schema: "procurement",
                table: "procurement_batches",
                columns: new[] { "procurement_date", "procurement_product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_procurement_entries_batch_id",
                schema: "procurement",
                table: "procurement_entries",
                column: "procurement_batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_procurement_entries_deleted_by_account_id",
                schema: "procurement",
                table: "procurement_entries",
                column: "deleted_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_procurement_entries_farmer_id",
                schema: "procurement",
                table: "procurement_entries",
                column: "farmer_id");

            migrationBuilder.CreateIndex(
                name: "ix_procurement_entries_recorded_by_account_id",
                schema: "procurement",
                table: "procurement_entries",
                column: "recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_procurement_entries_supplier_id",
                schema: "procurement",
                table: "procurement_entries",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ix_procurement_products_created_by_account_id",
                schema: "product",
                table: "procurement_products",
                column: "created_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_procurement_products_default_storage_location_id",
                schema: "product",
                table: "procurement_products",
                column: "default_storage_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_procurement_products_deleted_by_account_id",
                schema: "product",
                table: "procurement_products",
                column: "deleted_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipts_confirmed_by_account_id",
                schema: "finance",
                table: "receipts",
                column: "confirmed_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipts_receivable_id",
                schema: "finance",
                table: "receipts",
                column: "receivable_id");

            migrationBuilder.CreateIndex(
                name: "ix_receivable_obligation_items_detail_sales",
                schema: "finance",
                table: "receivable_obligation_items",
                columns: new[] { "sales_detail_id", "sales_id" });

            migrationBuilder.CreateIndex(
                name: "ix_receivable_obligation_items_receivable_sales",
                schema: "finance",
                table: "receivable_obligation_items",
                columns: new[] { "receivable_id", "sales_id" });

            migrationBuilder.CreateIndex(
                name: "ux_receivable_obligation_items_sales_detail_id",
                schema: "finance",
                table: "receivable_obligation_items",
                column: "sales_detail_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_receivables_sales_id",
                schema: "finance",
                table: "receivables",
                column: "sales_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_route_input_configs_container_id",
                schema: "processing_config",
                table: "route_input_configs",
                column: "container_id");

            migrationBuilder.CreateIndex(
                name: "ix_route_input_configs_default_storage_location_id",
                schema: "processing_config",
                table: "route_input_configs",
                column: "default_storage_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_confirmed_by_account_id",
                schema: "sales",
                table: "sales",
                column: "confirmed_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_created_by_account_id",
                schema: "sales",
                table: "sales",
                column: "created_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_customer_id",
                schema: "sales",
                table: "sales",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_deleted_by_account_id",
                schema: "sales",
                table: "sales",
                column: "deleted_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_allocation_revision_items_detail_sales",
                schema: "sales",
                table: "sales_allocation_revision_items",
                columns: new[] { "sales_detail_id", "sales_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_allocation_revision_items_outsourced_batch_id",
                schema: "sales",
                table: "sales_allocation_revision_items",
                column: "outsourced_supply_batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_allocation_revision_items_procurement_batch_id",
                schema: "sales",
                table: "sales_allocation_revision_items",
                column: "procurement_batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_allocation_revision_items_revision_sales",
                schema: "sales",
                table: "sales_allocation_revision_items",
                columns: new[] { "sales_allocation_revision_id", "sales_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_allocation_revision_items_sales_id",
                schema: "sales",
                table: "sales_allocation_revision_items",
                column: "sales_id");

            migrationBuilder.CreateIndex(
                name: "ux_sales_allocation_revision_items_revision_detail_sequence",
                schema: "sales",
                table: "sales_allocation_revision_items",
                columns: new[] { "sales_allocation_revision_id", "sales_detail_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_allocation_revisions_created_by_account_id",
                schema: "sales",
                table: "sales_allocation_revisions",
                column: "created_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_allocation_revisions_sales_id",
                schema: "sales",
                table: "sales_allocation_revisions",
                column: "sales_id");

            migrationBuilder.CreateIndex(
                name: "ux_sales_allocation_revisions_sales_revision_number",
                schema: "sales",
                table: "sales_allocation_revisions",
                columns: new[] { "sales_id", "revision_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_allocations_sales_allocation_revision_item_id_sales_d~",
                schema: "sales",
                table: "sales_allocations",
                columns: new[] { "sales_allocation_revision_item_id", "sales_detail_id" });

            migrationBuilder.CreateIndex(
                name: "ux_sales_allocations_revision_item_id",
                schema: "sales",
                table: "sales_allocations",
                column: "sales_allocation_revision_item_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_details_created_by_account_id",
                schema: "sales",
                table: "sales_details",
                column: "created_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_details_deleted_by_account_id",
                schema: "sales",
                table: "sales_details",
                column: "deleted_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_details_sales_id",
                schema: "sales",
                table: "sales_details",
                column: "sales_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_details_sales_product_id",
                schema: "sales",
                table: "sales_details",
                column: "sales_product_id");

            migrationBuilder.CreateIndex(
                name: "ux_sales_details_sales_line_number",
                schema: "sales",
                table: "sales_details",
                columns: new[] { "sales_id", "line_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_packaging_items_created_by_account_id",
                schema: "sales_handling",
                table: "sales_packaging_items",
                column: "created_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_packaging_items_deleted_by_account_id",
                schema: "sales_handling",
                table: "sales_packaging_items",
                column: "deleted_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_packaging_wage_components_daily_wage_id",
                schema: "labor",
                table: "sales_packaging_wage_components",
                column: "employee_daily_wage_id");

            migrationBuilder.CreateIndex(
                name: "ux_sales_packaging_wage_components_work_record",
                schema: "labor",
                table: "sales_packaging_wage_components",
                column: "sales_packaging_work_record_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_packaging_work_records_deleted_by_account_id",
                schema: "sales_handling",
                table: "sales_packaging_work_records",
                column: "deleted_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_packaging_work_records_employee_id",
                schema: "sales_handling",
                table: "sales_packaging_work_records",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_packaging_work_records_item_id",
                schema: "sales_handling",
                table: "sales_packaging_work_records",
                column: "sales_packaging_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_packaging_work_records_recorded_by_account_id",
                schema: "sales_handling",
                table: "sales_packaging_work_records",
                column: "recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_packaging_work_records_sales_id",
                schema: "sales_handling",
                table: "sales_packaging_work_records",
                column: "sales_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_product_groups_created_by_account_id",
                schema: "product",
                table: "sales_product_groups",
                column: "created_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_product_groups_deleted_by_account_id",
                schema: "product",
                table: "sales_product_groups",
                column: "deleted_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_products_created_by_account_id",
                schema: "product",
                table: "sales_products",
                column: "created_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_products_default_storage_location_id",
                schema: "product",
                table: "sales_products",
                column: "default_storage_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_products_deleted_by_account_id",
                schema: "product",
                table: "sales_products",
                column: "deleted_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_products_group_id",
                schema: "product",
                table: "sales_products",
                column: "sales_product_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_storage_locations_created_by_account_id",
                schema: "infrastructure",
                table: "storage_locations",
                column: "created_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_storage_locations_deleted_by_account_id",
                schema: "infrastructure",
                table: "storage_locations",
                column: "deleted_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_storage_locations_warehouse_id",
                schema: "infrastructure",
                table: "storage_locations",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "ix_suppliers_created_by_account_id",
                schema: "party",
                table: "suppliers",
                column: "created_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_suppliers_deleted_by_account_id",
                schema: "party",
                table: "suppliers",
                column: "deleted_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_warehouses_created_by_account_id",
                schema: "infrastructure",
                table: "warehouses",
                column: "created_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_warehouses_deleted_by_account_id",
                schema: "infrastructure",
                table: "warehouses",
                column: "deleted_by_account_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_event_subjects",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "command_executions",
                schema: "system");

            migrationBuilder.DropTable(
                name: "company_pickup_transport_obligation_basis_items",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "correction_links",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "inventory_movements",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "inventory_positions",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "system");

            migrationBuilder.DropTable(
                name: "payable_adjustments",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "payable_outstanding_positions",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "payments",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "processing_execution_inputs",
                schema: "processing");

            migrationBuilder.DropTable(
                name: "processing_wage_component_sources",
                schema: "labor");

            migrationBuilder.DropTable(
                name: "receipts",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "receivable_obligation_items",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "receivable_outstanding_positions",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "route_input_configs",
                schema: "processing_config");

            migrationBuilder.DropTable(
                name: "sales_allocations",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "sales_packaging_wage_components",
                schema: "labor");

            migrationBuilder.DropTable(
                name: "company_pickup_transport_bases",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "payable_obligation_items",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "audit_events",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "inventory_operations",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "processing_wage_components",
                schema: "labor");

            migrationBuilder.DropTable(
                name: "processing_execution_outputs",
                schema: "processing");

            migrationBuilder.DropTable(
                name: "receivables",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "sales_allocation_revision_items",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "sales_packaging_work_records",
                schema: "sales_handling");

            migrationBuilder.DropTable(
                name: "payables",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "procurement_entries",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "processing_executions",
                schema: "processing");

            migrationBuilder.DropTable(
                name: "processing_module_outputs",
                schema: "processing_config");

            migrationBuilder.DropTable(
                name: "sales_details",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "sales_allocation_revisions",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "sales_packaging_items",
                schema: "sales_handling");

            migrationBuilder.DropTable(
                name: "employee_daily_wages",
                schema: "labor");

            migrationBuilder.DropTable(
                name: "outsourced_supply_details",
                schema: "outsourced");

            migrationBuilder.DropTable(
                name: "farmers",
                schema: "party");

            migrationBuilder.DropTable(
                name: "procurement_batches",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "suppliers",
                schema: "party");

            migrationBuilder.DropTable(
                name: "processing_modules",
                schema: "processing_config");

            migrationBuilder.DropTable(
                name: "sales",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "employees",
                schema: "party");

            migrationBuilder.DropTable(
                name: "outsourced_supply_batches",
                schema: "outsourced");

            migrationBuilder.DropTable(
                name: "sales_products",
                schema: "product");

            migrationBuilder.DropTable(
                name: "process_materials",
                schema: "processing_config");

            migrationBuilder.DropTable(
                name: "customers",
                schema: "party");

            migrationBuilder.DropTable(
                name: "outsourced_vendors",
                schema: "party");

            migrationBuilder.DropTable(
                name: "sales_product_groups",
                schema: "product");

            migrationBuilder.DropTable(
                name: "containers",
                schema: "infrastructure");

            migrationBuilder.DropTable(
                name: "processing_route_versions",
                schema: "processing_config");

            migrationBuilder.DropTable(
                name: "processing_routes",
                schema: "processing_config");

            migrationBuilder.DropTable(
                name: "procurement_products",
                schema: "product");

            migrationBuilder.DropTable(
                name: "storage_locations",
                schema: "infrastructure");

            migrationBuilder.DropTable(
                name: "warehouses",
                schema: "infrastructure");

            migrationBuilder.DropTable(
                name: "accounts",
                schema: "system");
        }
    }
}
