using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Wms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Fbo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fbo_shipments",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    number = table.Column<string>(type: "text", nullable: false),
                    marketplace = table.Column<string>(type: "text", nullable: false),
                    destination = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    seller_id = table.Column<string>(type: "text", nullable: false),
                    warehouse_id = table.Column<string>(type: "text", nullable: false),
                    planned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    supply_id = table.Column<string>(type: "text", nullable: true),
                    seq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    history = table.Column<string>(type: "jsonb", nullable: true),
                    lines = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fbo_shipments", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fbo_shipments_number",
                table: "fbo_shipments",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fbo_shipments_seller_id",
                table: "fbo_shipments",
                column: "seller_id");

            migrationBuilder.CreateIndex(
                name: "ix_fbo_shipments_seq",
                table: "fbo_shipments",
                column: "seq");

            migrationBuilder.CreateIndex(
                name: "ix_fbo_shipments_warehouse_id",
                table: "fbo_shipments",
                column: "warehouse_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fbo_shipments");
        }
    }
}
