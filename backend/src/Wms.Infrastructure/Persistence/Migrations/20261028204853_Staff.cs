using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Staff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "staff_profiles",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    warehouse_id = table.Column<string>(type: "text", nullable: false),
                    zone_ids = table.Column<List<string>>(type: "text[]", nullable: false),
                    skills = table.Column<string>(type: "text", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    role_override = table.Column<string>(type: "text", nullable: true),
                    ops_per_hour = table.Column<int>(type: "integer", nullable: false),
                    accuracy = table.Column<double>(type: "double precision", nullable: false),
                    hired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    shifts = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_profiles", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_staff_profiles_warehouse_id",
                table: "staff_profiles",
                column: "warehouse_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "staff_profiles");
        }
    }
}
