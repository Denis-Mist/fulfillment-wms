using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Wms.Infrastructure.Persistence;

#nullable disable

namespace Wms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(WmsDbContext))]
    [Migration("20261106100000_TaskBatchMetrics")]
    public partial class TaskBatchMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "baseline_distance",
                table: "tasks",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "estimated_minutes",
                table: "tasks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "optimized_distance",
                table: "tasks",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "tote_capacity_liters",
                table: "tasks",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "volume_liters",
                table: "tasks",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "baseline_distance", table: "tasks");
            migrationBuilder.DropColumn(name: "estimated_minutes", table: "tasks");
            migrationBuilder.DropColumn(name: "optimized_distance", table: "tasks");
            migrationBuilder.DropColumn(name: "tote_capacity_liters", table: "tasks");
            migrationBuilder.DropColumn(name: "volume_liters", table: "tasks");
        }
    }
}
