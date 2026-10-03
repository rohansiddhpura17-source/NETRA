using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NETRA.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAreaRiskAssessments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RiskAssessments_CrimeCases_CrimeCaseId",
                table: "RiskAssessments");

            migrationBuilder.AlterColumn<Guid>(
                name: "CrimeCaseId",
                table: "RiskAssessments",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "DistrictId",
                table: "RiskAssessments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RiskAssessments_DistrictId",
                table: "RiskAssessments",
                column: "DistrictId");

            migrationBuilder.AddForeignKey(
                name: "FK_RiskAssessments_CrimeCases_CrimeCaseId",
                table: "RiskAssessments",
                column: "CrimeCaseId",
                principalTable: "CrimeCases",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_RiskAssessments_Districts_DistrictId",
                table: "RiskAssessments",
                column: "DistrictId",
                principalTable: "Districts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RiskAssessments_CrimeCases_CrimeCaseId",
                table: "RiskAssessments");

            migrationBuilder.DropForeignKey(
                name: "FK_RiskAssessments_Districts_DistrictId",
                table: "RiskAssessments");

            migrationBuilder.DropIndex(
                name: "IX_RiskAssessments_DistrictId",
                table: "RiskAssessments");

            migrationBuilder.DropColumn(
                name: "DistrictId",
                table: "RiskAssessments");

            migrationBuilder.AlterColumn<Guid>(
                name: "CrimeCaseId",
                table: "RiskAssessments",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_RiskAssessments_CrimeCases_CrimeCaseId",
                table: "RiskAssessments",
                column: "CrimeCaseId",
                principalTable: "CrimeCases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
