using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using NETRA.Infrastructure.Data;
using NETRA.Domain.Security;
using NETRA.Domain.Cases;
using NETRA.Domain.People;
using NETRA.Domain.Geography;
using NETRA.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using FluentAssertions;
using Xunit;

namespace NETRA.Tests.Infrastructure;

public class NetraDbContextTests : IDisposable
{
    private readonly NetraDbContext _context;

    public NetraDbContextTests()
    {
        var options = new DbContextOptionsBuilder<NetraDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new NetraDbContext(options);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public void NetraDbContext_ShouldHaveAllDbSets()
    {
        _context.Departments.Should().NotBeNull();
        _context.CrimeCases.Should().NotBeNull();
        _context.CrimeTypes.Should().NotBeNull();
        _context.CasePersons.Should().NotBeNull();
        _context.CaseLocations.Should().NotBeNull();
        _context.Persons.Should().NotBeNull();
        _context.PersonAliases.Should().NotBeNull();
        _context.PersonLocations.Should().NotBeNull();
        _context.Locations.Should().NotBeNull();
        _context.Districts.Should().NotBeNull();
        _context.PoliceStations.Should().NotBeNull();
        _context.Investigations.Should().NotBeNull();
        _context.InvestigationNotes.Should().NotBeNull();
        _context.TimelineEvents.Should().NotBeNull();
        _context.Evidence.Should().NotBeNull();
        _context.EvidenceFiles.Should().NotBeNull();
        _context.RiskAssessments.Should().NotBeNull();
        _context.Anomalies.Should().NotBeNull();
        _context.NetworkEntities.Should().NotBeNull();
        _context.NetworkRelationships.Should().NotBeNull();
        _context.Alerts.Should().NotBeNull();
        _context.Notifications.Should().NotBeNull();
        _context.ImageRecords.Should().NotBeNull();
        _context.ImageMatches.Should().NotBeNull();
        _context.Reports.Should().NotBeNull();
        _context.ImportJobs.Should().NotBeNull();
        _context.ImportRecords.Should().NotBeNull();
        _context.ImportErrors.Should().NotBeNull();
        _context.AuditLogs.Should().NotBeNull();
        _context.UserSettings.Should().NotBeNull();
        _context.SecuritySettings.Should().NotBeNull();
        _context.NotificationPreferences.Should().NotBeNull();
        _context.SystemPreferences.Should().NotBeNull();
        _context.AssistantSessions.Should().NotBeNull();
        _context.AssistantMessages.Should().NotBeNull();
    }

    [Fact]
    public async Task NetraDbContext_CanAddAndRetrieveDepartment()
    {
        var dept = new Department
        {
            Name = "Test Department",
            Code = "TST",
            Description = "Test department",
            IsActive = true
        };

        _context.Departments.Add(dept);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Departments.FindAsync(dept.Id);
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("Test Department");
        retrieved.Code.Should().Be("TST");
    }

    [Fact]
    public async Task NetraDbContext_CanAddAndRetrieveCrimeCase()
    {
        var crimeType = new CrimeType
        {
            Name = "Test Crime",
            Code = "TST",
            Description = "Test crime type",
            DisplayOrder = 1,
            IsActive = true
        };
        _context.CrimeTypes.Add(crimeType);
        await _context.SaveChangesAsync();

        var crimeCase = new CrimeCase
        {
            CaseNumber = "CC-2024-001",
            Title = "Test Case",
            Description = "Test case description",
            CrimeTypeId = crimeType.Id,
            Status = CaseStatus.Open,
            Severity = CaseSeverity.Medium,
            IncidentDate = DateTime.UtcNow.AddDays(-1),
            CreatedById = Guid.NewGuid()
        };

        _context.CrimeCases.Add(crimeCase);
        await _context.SaveChangesAsync();

        var retrieved = await _context.CrimeCases.FindAsync(crimeCase.Id);
        retrieved.Should().NotBeNull();
        retrieved!.CaseNumber.Should().Be("CC-2024-001");
        retrieved.CrimeTypeId.Should().Be(crimeType.Id);
    }

    [Fact]
    public async Task NetraDbContext_CanAddAndRetrievePerson()
    {
        var person = new Person
        {
            FirstName = "Test",
            LastName = "Person",
            DateOfBirth = new DateTime(1990, 1, 1),
            Gender = "Male",
            NationalIdNumber = "NID123456",
            Email = "test.person@example.com",
            IsActive = true,
            CreatedById = Guid.NewGuid()
        };

        _context.Persons.Add(person);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Persons.FindAsync(person.Id);
        retrieved.Should().NotBeNull();
        retrieved!.FirstName.Should().Be("Test");
        retrieved.LastName.Should().Be("Person");
        retrieved.FullName.Should().Be("Test Person");
    }

    [Fact]
    public async Task NetraDbContext_CanAddAndRetrieveDistrict()
    {
        var district = new District
        {
            Name = "Test District",
            Code = "TD",
            Description = "Test district",
            IsActive = true
        };

        _context.Districts.Add(district);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Districts.FindAsync(district.Id);
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("Test District");
        retrieved.Code.Should().Be("TD");
    }

    [Fact]
    public async Task NetraDbContext_CanAddAndRetrievePoliceStation()
    {
        var district = new District
        {
            Name = "Test District",
            Code = "TD",
            Description = "Test district",
            IsActive = true
        };
        _context.Districts.Add(district);
        await _context.SaveChangesAsync();

        var station = new PoliceStation
        {
            Name = "Test Station",
            Code = "TS",
            Address = "123 Test St",
            PhoneNumber = "555-0000",
            Email = "test@police.gov",
            DistrictId = district.Id,
            IsActive = true
        };

        _context.PoliceStations.Add(station);
        await _context.SaveChangesAsync();

        var retrieved = await _context.PoliceStations.FindAsync(station.Id);
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("Test Station");
        retrieved.DistrictId.Should().Be(district.Id);
    }

    [Fact]
    public void NetraDbContext_ShouldInheritFromIdentityDbContext()
    {
        _context.Should().BeAssignableTo<IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>>();
    }
}