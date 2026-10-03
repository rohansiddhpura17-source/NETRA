using Microsoft.EntityFrameworkCore;
using NETRA.Infrastructure.Data;
using NETRA.Infrastructure.Data.Configurations;
using NETRA.Domain.Security;
using NETRA.Domain.Cases;
using NETRA.Domain.People;
using NETRA.Domain.Geography;
using NETRA.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace NETRA.Tests.Infrastructure;

public class ConfigurationTests
{
    [Fact]
    public void ApplicationUserConfiguration_ShouldMapCorrectly()
    {
        var builder = new ModelBuilder();
        var config = new ApplicationUserConfiguration();
        config.Configure(builder.Entity<ApplicationUser>());

        var entityType = builder.Model.FindEntityType(typeof(ApplicationUser));
        entityType.Should().NotBeNull();
        entityType!.GetTableName().Should().Be("ApplicationUsers");
    }

    [Fact]
    public void DepartmentConfiguration_ShouldMapCorrectly()
    {
        var builder = new ModelBuilder();
        var config = new DepartmentConfiguration();
        config.Configure(builder.Entity<Department>());

        var entityType = builder.Model.FindEntityType(typeof(Department));
        entityType.Should().NotBeNull();
        entityType!.GetTableName().Should().Be("Departments");
    }

    [Fact]
    public void CrimeCaseConfiguration_ShouldMapCorrectly()
    {
        var builder = new ModelBuilder();
        var config = new CrimeCaseConfiguration();
        config.Configure(builder.Entity<CrimeCase>());

        var entityType = builder.Model.FindEntityType(typeof(CrimeCase));
        entityType.Should().NotBeNull();
        entityType!.GetTableName().Should().Be("CrimeCases");
    }

    [Fact]
    public void CrimeTypeConfiguration_ShouldMapCorrectly()
    {
        var builder = new ModelBuilder();
        var config = new CrimeTypeConfiguration();
        config.Configure(builder.Entity<CrimeType>());

        var entityType = builder.Model.FindEntityType(typeof(CrimeType));
        entityType.Should().NotBeNull();
        entityType!.GetTableName().Should().Be("CrimeTypes");
    }

    [Fact]
    public void PersonConfiguration_ShouldMapCorrectly()
    {
        var builder = new ModelBuilder();
        var config = new PersonConfiguration();
        config.Configure(builder.Entity<Person>());

        var entityType = builder.Model.FindEntityType(typeof(Person));
        entityType.Should().NotBeNull();
        entityType!.GetTableName().Should().Be("Persons");
    }

    [Fact]
    public void LocationConfiguration_ShouldMapCorrectly()
    {
        var builder = new ModelBuilder();
        var config = new LocationConfiguration();
        config.Configure(builder.Entity<Location>());

        var entityType = builder.Model.FindEntityType(typeof(Location));
        entityType.Should().NotBeNull();
        entityType!.GetTableName().Should().Be("Locations");
    }

    [Fact]
    public void DistrictConfiguration_ShouldMapCorrectly()
    {
        var builder = new ModelBuilder();
        var config = new DistrictConfiguration();
        config.Configure(builder.Entity<District>());

        var entityType = builder.Model.FindEntityType(typeof(District));
        entityType.Should().NotBeNull();
        entityType!.GetTableName().Should().Be("Districts");
    }

    [Fact]
    public void PoliceStationConfiguration_ShouldMapCorrectly()
    {
        var builder = new ModelBuilder();
        var config = new PoliceStationConfiguration();
        config.Configure(builder.Entity<PoliceStation>());

        var entityType = builder.Model.FindEntityType(typeof(PoliceStation));
        entityType.Should().NotBeNull();
        entityType!.GetTableName().Should().Be("PoliceStations");
    }
}