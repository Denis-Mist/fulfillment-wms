using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MarketplaceCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "api_key_cipher",
                table: "marketplace_integrations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "environment",
                table: "marketplace_integrations",
                type: "text",
                nullable: false,
                defaultValue: "sandbox");

            migrationBuilder.AddColumn<string>(
                name: "last_error",
                table: "marketplace_integrations",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "last_http_status",
                table: "marketplace_integrations",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "api_key_cipher",
                table: "marketplace_integrations");

            migrationBuilder.DropColumn(
                name: "environment",
                table: "marketplace_integrations");

            migrationBuilder.DropColumn(
                name: "last_error",
                table: "marketplace_integrations");

            migrationBuilder.DropColumn(
                name: "last_http_status",
                table: "marketplace_integrations");
        }
    }
}
