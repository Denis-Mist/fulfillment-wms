using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Wms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WarehousesAndCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "seq",
                table: "warehouses",
                type: "bigint",
                nullable: false)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.CreateTable(
                name: "locations",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    warehouse_id = table.Column<string>(type: "text", nullable: false),
                    zone_id = table.Column<string>(type: "text", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    x = table.Column<double>(type: "double precision", nullable: false),
                    y = table.Column<double>(type: "double precision", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    capacity_liters = table.Column<double>(type: "double precision", nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_locations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    seller_id = table.Column<string>(type: "text", nullable: false),
                    sku = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    barcode = table.Column<string>(type: "text", nullable: false),
                    wb_barcode = table.Column<string>(type: "text", nullable: true),
                    price = table.Column<double>(type: "double precision", nullable: false),
                    weight_g = table.Column<double>(type: "double precision", nullable: false),
                    volume_liters = table.Column<double>(type: "double precision", nullable: false),
                    tracks_expiry = table.Column<bool>(type: "boolean", nullable: false),
                    requires_marking = table.Column<bool>(type: "boolean", nullable: false),
                    archived = table.Column<bool>(type: "boolean", nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    product_id = table.Column<string>(type: "text", nullable: false),
                    seller_id = table.Column<string>(type: "text", nullable: false),
                    warehouse_id = table.Column<string>(type: "text", nullable: false),
                    location_id = table.Column<string>(type: "text", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    reserved = table.Column<int>(type: "integer", nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "zones",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    warehouse_id = table.Column<string>(type: "text", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_zones", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_warehouses_seq",
                table: "warehouses",
                column: "seq");

            migrationBuilder.CreateIndex(
                name: "ix_locations_seq",
                table: "locations",
                column: "seq");

            migrationBuilder.CreateIndex(
                name: "ix_locations_warehouse_id_code",
                table: "locations",
                columns: new[] { "warehouse_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_locations_zone_id",
                table: "locations",
                column: "zone_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_seller_id",
                table: "products",
                column: "seller_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_seq",
                table: "products",
                column: "seq");

            migrationBuilder.CreateIndex(
                name: "ix_stock_location_id",
                table: "stock",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_product_id",
                table: "stock",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_seq",
                table: "stock",
                column: "seq");

            migrationBuilder.CreateIndex(
                name: "ix_stock_warehouse_id",
                table: "stock",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "ix_zones_seq",
                table: "zones",
                column: "seq");

            migrationBuilder.CreateIndex(
                name: "ix_zones_warehouse_id_code",
                table: "zones",
                columns: new[] { "warehouse_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "locations");

            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "stock");

            migrationBuilder.DropTable(
                name: "zones");

            migrationBuilder.DropIndex(
                name: "ix_warehouses_seq",
                table: "warehouses");

            migrationBuilder.DropColumn(
                name: "seq",
                table: "warehouses");
        }
    }
}
