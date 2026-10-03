using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NETRA.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertDeduplicationKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AlertKey",
                table: "Alerts",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WindowEnd",
                table: "Alerts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WindowStart",
                table: "Alerts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_AlertKey",
                table: "Alerts",
                column: "AlertKey",
                unique: true,
                filter: "[AlertKey] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Alerts_AlertKey",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "AlertKey",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "WindowEnd",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "WindowStart",
                table: "Alerts");
        }
    }
}
