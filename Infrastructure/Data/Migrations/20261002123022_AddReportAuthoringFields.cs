using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NETRA.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReportAuthoringFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "Reports",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Content",
                table: "Reports",
                type: "nvarchar(max)",
                maxLength: 8000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PeriodFrom",
                table: "Reports",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PeriodTo",
                table: "Reports",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "Reports",
                type: "int",
                nullable: false,
                defaultValue: 2); // 2 = AnalystAuthored: pre-Phase 11 rows were manual records

            migrationBuilder.CreateIndex(
                name: "IX_Reports_Source",
                table: "Reports",
                column: "Source");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reports_Source",
                table: "Reports");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "Reports");

            migrationBuilder.DropColumn(
                name: "Content",
                table: "Reports");

            migrationBuilder.DropColumn(
                name: "PeriodFrom",
                table: "Reports");

            migrationBuilder.DropColumn(
                name: "PeriodTo",
                table: "Reports");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Reports");
        }
    }
}
