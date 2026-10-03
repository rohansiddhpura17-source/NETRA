using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using NETRA.Infrastructure.Data;
using NETRA.Domain.Cases;
using NETRA.Domain.People;
using NETRA.Domain.Geography;
using NETRA.Domain.Intelligence;
using FluentAssertions;
using Xunit;

namespace NETRA.Tests.Infrastructure;

public class ModelShadowPropertyTests
{
    private static NetraDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<NetraDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new NetraDbContext(options);
    }

    private static bool HasUniqueIndexOn(IReadOnlyEntityType entityType, string propertyName)
    {
        return entityType.GetIndexes()
            .Any(i => i.IsUnique && i.Properties.Any(p => p.Name == propertyName));
    }

    [Fact]
    public void Model_ShouldNotContainAnyShadowProperties()
    {
        using var context = CreateContext();

        var shadowProperties = context.Model
            .GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => p.IsShadowProperty())
            .Select(p => $"{p.DeclaringType.DisplayName()}.{p.Name}")
            .ToList();

        shadowProperties.Should().BeEmpty(
            "shadow properties indicate ambiguous or unmapped foreign key relationships");
    }

    [Fact]
    public void CaseLocation_ShouldHaveExactlyOneForeignKeyToLocation()
    {
        using var context = CreateContext();

        var entityType = context.Model.FindEntityType(typeof(CaseLocation))!;
        var foreignKeys = entityType.GetForeignKeys()
            .Where(fk => fk.PrincipalEntityType.ClrType == typeof(Location))
            .ToList();

        foreignKeys.Should().HaveCount(1);
        foreignKeys[0].Properties.Should().ContainSingle().Which.Name.Should().Be("LocationId");
    }

    [Fact]
    public void PersonLocation_ShouldHaveExactlyOneForeignKeyToLocation()
    {
        using var context = CreateContext();

        var entityType = context.Model.FindEntityType(typeof(PersonLocation))!;
        var foreignKeys = entityType.GetForeignKeys()
            .Where(fk => fk.PrincipalEntityType.ClrType == typeof(Location))
            .ToList();

        foreignKeys.Should().HaveCount(1);
        foreignKeys[0].Properties.Should().ContainSingle().Which.Name.Should().Be("LocationId");
    }

    [Fact]
    public void Location_ShouldExposeCaseLocationsAndPersonLocationsAsNavigations()
    {
        using var context = CreateContext();

        var locationType = context.Model.FindEntityType(typeof(Location))!;

        locationType.GetNavigations()
            .Where(n => n.TargetEntityType.ClrType == typeof(CaseLocation))
            .Should().ContainSingle().Which.Name.Should().Be("CaseLocations");

        locationType.GetNavigations()
            .Where(n => n.TargetEntityType.ClrType == typeof(PersonLocation))
            .Should().ContainSingle().Which.Name.Should().Be("PersonLocations");
    }

    [Fact]
    public void CaseLocation_LocationForeignKey_ShouldBeRequiredAndCascadeDelete()
    {
        using var context = CreateContext();

        var entityType = context.Model.FindEntityType(typeof(CaseLocation))!;
        var foreignKey = entityType.GetForeignKeys()
            .Single(fk => fk.PrincipalEntityType.ClrType == typeof(Location));

        foreignKey.IsRequired.Should().BeTrue();
        foreignKey.DeleteBehavior.Should().Be(DeleteBehavior.Cascade);
    }

    [Fact]
    public void PersonLocation_LocationForeignKey_ShouldBeRequiredAndCascadeDelete()
    {
        using var context = CreateContext();

        var entityType = context.Model.FindEntityType(typeof(PersonLocation))!;
        var foreignKey = entityType.GetForeignKeys()
            .Single(fk => fk.PrincipalEntityType.ClrType == typeof(Location));

        foreignKey.IsRequired.Should().BeTrue();
        foreignKey.DeleteBehavior.Should().Be(DeleteBehavior.Cascade);
    }

    [Fact]
    public void CrimeCase_CaseNumber_ShouldBeRequiredUniqueAndIndexed()
    {
        using var context = CreateContext();

        var entityType = context.Model.FindEntityType(typeof(CrimeCase))!;
        var caseNumber = entityType.FindProperty(nameof(CrimeCase.CaseNumber))!;

        caseNumber.IsNullable.Should().BeFalse();
        caseNumber.GetMaxLength().Should().Be(50);
        HasUniqueIndexOn(entityType, nameof(CrimeCase.CaseNumber)).Should().BeTrue();
    }

    [Fact]
    public void District_And_PoliceStation_Codes_ShouldBeUnique()
    {
        using var context = CreateContext();

        var district = context.Model.FindEntityType(typeof(District))!;
        HasUniqueIndexOn(district, nameof(District.Code)).Should().BeTrue();

        var station = context.Model.FindEntityType(typeof(PoliceStation))!;
        HasUniqueIndexOn(station, nameof(PoliceStation.Code)).Should().BeTrue();
    }

    [Fact]
    public void AuditLog_ShouldBeIndexedByTimestampAndUserId()
    {
        using var context = CreateContext();

        var entityType = context.Model.FindEntityType(typeof(NETRA.Domain.Audit.AuditLog))!;
        var indexProperties = entityType.GetIndexes()
            .SelectMany(i => i.Properties)
            .Select(p => p.Name)
            .ToList();

        indexProperties.Should().Contain(nameof(NETRA.Domain.Audit.AuditLog.Timestamp));
        indexProperties.Should().Contain(nameof(NETRA.Domain.Audit.AuditLog.UserId));
    }

    [Fact]
    public void NetworkEntity_ShouldRequireExactlyOneRealEntityReference()
    {
        using var context = CreateContext();

        // Check constraint configuration is stored on the design-time model only;
        // the read-optimized runtime model omits it.
        var designTimeModel = context.GetService<IDesignTimeModel>().Model;
        var entityType = designTimeModel.FindEntityType(typeof(NetworkEntity))!;
        entityType.GetCheckConstraints()
            .Should().ContainSingle(constraint =>
                constraint.Name == "CK_NetworkEntities_ExactlyOneReference"
                && constraint.Sql.Contains("PersonId")
                && constraint.Sql.Contains("CrimeCaseId")
                && constraint.Sql.Contains("LocationId"));
    }
}
