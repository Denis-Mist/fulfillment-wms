using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Wms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Returns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_returns",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    number = table.Column<string>(type: "text", nullable: false),
                    order_id = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    seller_id = table.Column<string>(type: "text", nullable: false),
                    warehouse_id = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    inspected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    inspected_by = table.Column<string>(type: "text", nullable: true),
                    comment = table.Column<string>(type: "text", nullable: true),
                    photos = table.Column<int>(type: "integer", nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    lines = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_returns", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_customer_returns_number",
                table: "customer_returns",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_customer_returns_seller_id",
                table: "customer_returns",
                column: "seller_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_returns_seq",
                table: "customer_returns",
                column: "seq");

            migrationBuilder.CreateIndex(
                name: "ix_customer_returns_warehouse_id",
                table: "customer_returns",
                column: "warehouse_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_returns");
        }
    }
}
