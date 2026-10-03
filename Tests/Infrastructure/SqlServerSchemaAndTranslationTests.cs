using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NETRA.Infrastructure.Data;
using NETRA.Infrastructure.Services;

namespace NETRA.Tests.Infrastructure;

/// <summary>
/// Phase 15 SQL Server verification that needs no database server. Every check here runs the
/// real <c>Microsoft.EntityFrameworkCore.SqlServer</c> provider: the model is compiled to a
/// create script and query shapes are compiled to command text, both without opening a
/// connection. These are deliberately separate from the InMemory suites because InMemory does
/// not enforce column widths, unique or check constraints, transactions, or SQL translation.
/// </summary>
/// <remarks>
/// <see cref="RelationalDatabaseFacadeExtensions.GenerateCreateScript"/> renders the current
/// model, so it proves what the schema must contain; that the migration chain actually reaches
/// it is guarded by <see cref="EveryMigrationType_IsDiscoveredByTheProvider"/> and was checked
/// line by line against <c>dotnet ef migrations script</c> during Phase 15.
/// </remarks>
public class SqlServerSchemaAndTranslationTests
{
    /// <summary>Never contacted: the provider only needs a syntactically valid data source.</summary>
    private const string UnconnectedConnectionString =
        "Server=netra-schema-probe.invalid;Database=NETRASchemaProbe;Connect Timeout=1";

    private static NetraDbContext ProbeContext() =>
        new(new DbContextOptionsBuilder<NetraDbContext>()
            .UseSqlServer(UnconnectedConnectionString)
            .Options);

    private static string SchemaScript()
    {
        using var context = ProbeContext();
        return context.Database.GenerateCreateScript();
    }

    [Fact]
    public void EveryMigrationType_IsDiscoveredByTheProvider()
    {
        // A migration class without its generated [Migration] attribute is invisible to
        // Database.MigrateAsync: it is skipped silently and the model snapshot already counts
        // it as applied, so its schema change never reaches a real database.
        var undiscoverable = typeof(NetraDbContext).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: true } && typeof(Migration).IsAssignableFrom(t))
            .Where(t => t.GetCustomAttributes(typeof(MigrationAttribute), inherit: false).Length == 0)
            .Select(t => t.Name)
            .ToList();

        undiscoverable.Should().BeEmpty();
    }

    [Fact]
    public void Migrations_AreOrderedAndFreeOfDuplicates()
    {
        var ids = typeof(NetraDbContext).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: true } && typeof(Migration).IsAssignableFrom(t))
            .Select(t => ((MigrationAttribute)t.GetCustomAttributes(typeof(MigrationAttribute), false)[0]).Id)
            .ToList();

        ids.Should().OnlyHaveUniqueItems();
        ids.Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public void CreateScript_ContainsEveryMappedTable()
    {
        var script = SchemaScript();

        var expected = new[]
        {
            "CrimeCases", "Persons", "Locations", "Districts", "Departments", "PoliceStations",
            "CrimeTypes", "Evidence", "Investigations", "InvestigationNotes", "TimelineEvents",
            "CasePersons", "CaseLocations", "PersonLocations", "PersonAliases",
            "NetworkEntities", "NetworkRelationships", "ImageRecords", "ImageMatches",
            "RiskAssessments", "Anomalies", "Alerts", "Notifications", "Reports",
            "AssistantSessions", "AssistantMessages",
            "ImportJobs", "ImportRecords", "ImportErrors",
            "AuditLogs", "ApplicationUsers", "AspNetRoles", "AspNetUserRoles", "AspNetUserClaims",
            "AspNetRoleClaims", "AspNetUserLogins", "AspNetUserTokens",
            "UserSettings", "SecuritySettings", "SystemPreferences", "NotificationPreferences",
        };

        expected.Should().OnlyContain(name => script.Contains($"CREATE TABLE [{name}]", StringComparison.Ordinal),
            because: "a database built from the migrations must contain every module's table");
    }

    [Fact]
    public void CreateScript_AppliesTheNetworkReferenceCheckConstraint()
    {
        // The constraint is declared in the model, so only a discoverable migration can put it
        // into a real database; a missing one is invisible to every InMemory test.
        SchemaScript().Should().Contain("CK_NetworkEntities_ExactlyOneReference");
    }

    [Fact]
    public void CreateScript_KeepsTheAuditTrailIndependentOfCases()
    {
        var script = SchemaScript();

        // Audit rows describe whatever they recorded, including entities that may later be
        // deleted, so the audit log references its subject by value and keeps only a real
        // foreign key to the account that acted.
        script.Should().Contain("FK_AuditLogs_ApplicationUsers_UserId");

        using var context = ProbeContext();
        var principals = context.Model.FindEntityType(typeof(NETRA.Domain.Audit.AuditLog))!
            .GetForeignKeys()
            .Select(fk => fk.PrincipalEntityType.ClrType.Name);

        principals.Should().NotContain("CrimeCase");
    }

    [Fact]
    public void CreateScript_EnforcesCaseNumberUniquenessAtTheDatabase()
    {
        SchemaScript().Should().Contain("CREATE UNIQUE INDEX [IX_CrimeCases_CaseNumber]");
    }

    [Fact]
    public void CreateScript_MarksTheFilteredPersonLocationIndexUnique()
    {
        // SQL Server treats NULLs as distinct in a unique index, so the filter is what makes
        // "unique while dated" hold; without it the intent silently disappears.
        SchemaScript().Should().Contain(
            "CREATE UNIQUE INDEX [IX_PersonLocations_PersonId_LocationId_FromDate]");
    }

    [Fact]
    public void SqlServerProvider_TranslatesTheProjectionShapesTheServicesRelyOn()
    {
        using var context = ProbeContext();

        // Enum names and trimmed names are produced inside server-side projections. If the
        // provider ever stops translating them the case list fails at runtime, and InMemory
        // would keep passing.
        var status = context.CrimeCases.Select(c => c.Status.ToString()).ToQueryString();
        status.Should().Contain("N'Open'").And.Contain("N'UnderInvestigation'");

        var investigator = context.CrimeCases
            .Select(c => (c.AssignedInvestigator!.FirstName + " " + c.AssignedInvestigator.LastName).Trim())
            .ToQueryString();
        investigator.Should().Contain("LTRIM(RTRIM(");

        var paged = context.CrimeCases.Skip(10).Take(25).Select(c => c.Title).ToQueryString();
        paged.Should().Contain("OFFSET").And.Contain("ORDER BY");
    }

    [Fact]
    public void SqlServerProvider_TranslatesTheIncidentLocationLookup()
    {
        using var context = ProbeContext();

        var sql = context.CrimeCases
            .Select(c => c.CaseLocations
                .Where(cl => cl.LocationRole == CaseService.IncidentLocationRole)
                .Select(cl => cl.Location!.District!.Name)
                .FirstOrDefault() ?? "-")
            .ToQueryString();

        sql.Should().Contain("TOP(1)").And.Contain("[LocationRole] = N'Incident'");
    }
}
