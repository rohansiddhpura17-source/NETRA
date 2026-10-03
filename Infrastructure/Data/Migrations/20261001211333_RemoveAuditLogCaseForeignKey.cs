using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NETRA.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAuditLogCaseForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AuditLogs_CrimeCases_EntityId",
                table: "AuditLogs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_AuditLogs_CrimeCases_EntityId",
                table: "AuditLogs",
                column: "EntityId",
                principalTable: "CrimeCases",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
