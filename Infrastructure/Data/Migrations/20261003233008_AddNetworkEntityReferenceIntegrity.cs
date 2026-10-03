using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NETRA.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNetworkEntityReferenceIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Keeps NetworkEntity as a reference to exactly one real Person, CrimeCase or
            // Location. The existing foreign keys verify the referenced record exists; this
            // constraint prevents an empty or multiply-referenced wrapper row.
            migrationBuilder.AddCheckConstraint(
                name: "CK_NetworkEntities_ExactlyOneReference",
                table: "NetworkEntities",
                sql: "(CASE WHEN [PersonId] IS NULL THEN 0 ELSE 1 END + " +
                     "CASE WHEN [CrimeCaseId] IS NULL THEN 0 ELSE 1 END + " +
                     "CASE WHEN [LocationId] IS NULL THEN 0 ELSE 1 END) = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_NetworkEntities_ExactlyOneReference",
                table: "NetworkEntities");
        }
    }
}
