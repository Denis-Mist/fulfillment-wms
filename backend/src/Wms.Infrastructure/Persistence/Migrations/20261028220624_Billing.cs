using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Billing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "billing_acts",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    contract_id = table.Column<string>(type: "text", nullable: false),
                    period = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_billing_acts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "marketplace_integrations",
                columns: table => new
                {
                    organization_id = table.Column<string>(type: "text", nullable: false),
                    channel = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    key_mask = table.Column<string>(type: "text", nullable: true),
                    client_id = table.Column<string>(type: "text", nullable: true),
                    connected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_sync_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    auto_import_fbs = table.Column<bool>(type: "boolean", nullable: false),
                    push_stock = table.Column<bool>(type: "boolean", nullable: false),
                    sync_interval_min = table.Column<int>(type: "integer", nullable: false),
                    log = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_marketplace_integrations", x => new { x.organization_id, x.channel });
                });

            migrationBuilder.CreateTable(
                name: "tariffs",
                columns: table => new
                {
                    contract_id = table.Column<string>(type: "text", nullable: false),
                    receiving_per_unit = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    storage_per_m3day = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    pick_per_order = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    pack_per_unit = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    fbo_per_box = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    return_per_unit = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tariffs", x => x.contract_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_billing_acts_contract_id_period",
                table: "billing_acts",
                columns: new[] { "contract_id", "period" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "billing_acts");

            migrationBuilder.DropTable(
                name: "marketplace_integrations");

            migrationBuilder.DropTable(
                name: "tariffs");
        }
    }
}
