using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Wms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InboundAndTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inbound_shipments",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    number = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    seller_id = table.Column<string>(type: "text", nullable: false),
                    warehouse_id = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    package_type = table.Column<string>(type: "text", nullable: false),
                    packages_count = table.Column<int>(type: "integer", nullable: false),
                    comment = table.Column<string>(type: "text", nullable: true),
                    driver_name = table.Column<string>(type: "text", nullable: true),
                    vehicle_number = table.Column<string>(type: "text", nullable: true),
                    seq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    events = table.Column<string>(type: "jsonb", nullable: true),
                    lines = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbound_shipments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tasks",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    number = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    priority = table.Column<string>(type: "text", nullable: false),
                    warehouse_id = table.Column<string>(type: "text", nullable: false),
                    organization_id = table.Column<string>(type: "text", nullable: false),
                    assignee_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    inbound_id = table.Column<string>(type: "text", nullable: true),
                    algorithm = table.Column<string>(type: "text", nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    items = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tasks", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inbound_shipments_number",
                table: "inbound_shipments",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inbound_shipments_seller_id",
                table: "inbound_shipments",
                column: "seller_id");

            migrationBuilder.CreateIndex(
                name: "ix_inbound_shipments_seq",
                table: "inbound_shipments",
                column: "seq");

            migrationBuilder.CreateIndex(
                name: "ix_inbound_shipments_warehouse_id",
                table: "inbound_shipments",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_inbound_id",
                table: "tasks",
                column: "inbound_id");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_seq",
                table: "tasks",
                column: "seq");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_warehouse_id",
                table: "tasks",
                column: "warehouse_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbound_shipments");

            migrationBuilder.DropTable(
                name: "tasks");
        }
    }
}
