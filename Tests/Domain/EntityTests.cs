using NETRA.Domain.Security;
using NETRA.Domain.Cases;
using NETRA.Domain.People;
using NETRA.Domain.Geography;
using NETRA.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace NETRA.Tests.Domain;

public class EntityTests
{
    [Fact]
    public void ApplicationUser_ShouldHaveRequiredProperties()
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "test@netra.local",
            Email = "test@netra.local",
            FirstName = "John",
            LastName = "Doe",
            BadgeNumber = "12345",
            Rank = "Detective",
            IsActive = true
        };

        user.Id.Should().NotBeEmpty();
        user.UserName.Should().Be("test@netra.local");
        user.Email.Should().Be("test@netra.local");
        user.FirstName.Should().Be("John");
        user.LastName.Should().Be("Doe");
        user.FullName.Should().Be("John Doe");
        user.BadgeNumber.Should().Be("12345");
        user.Rank.Should().Be("Detective");
        user.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Department_ShouldHaveRequiredProperties()
    {
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Name = "Homicide Division",
            Code = "HOM",
            Description = "Handles homicide cases",
            IsActive = true
        };

        dept.Id.Should().NotBeEmpty();
        dept.Name.Should().Be("Homicide Division");
        dept.Code.Should().Be("HOM");
        dept.IsActive.Should().BeTrue();
    }

    [Fact]
    public void CrimeCase_ShouldHaveRequiredProperties()
    {
        var crimeCase = new CrimeCase
        {
            Id = Guid.NewGuid(),
            CaseNumber = "CC-2024-001",
            Title = "Test Case",
            Description = "Test case description",
            CrimeTypeId = Guid.NewGuid(),
            Status = CaseStatus.Open,
            Severity = CaseSeverity.High,
            IncidentDate = DateTime.UtcNow.AddDays(-1),
            CreatedById = Guid.NewGuid()
        };

        crimeCase.Id.Should().NotBeEmpty();
        crimeCase.CaseNumber.Should().Be("CC-2024-001");
        crimeCase.Title.Should().Be("Test Case");
        crimeCase.Status.Should().Be(CaseStatus.Open);
        crimeCase.Severity.Should().Be(CaseSeverity.High);
    }

    [Fact]
    public void CrimeType_ShouldHaveRequiredProperties()
    {
        var crimeType = new CrimeType
        {
            Id = Guid.NewGuid(),
            Name = "Homicide",
            Code = "HOM",
            Description = "Unlawful killing",
            DisplayOrder = 1,
            IsActive = true
        };

        crimeType.Id.Should().NotBeEmpty();
        crimeType.Name.Should().Be("Homicide");
        crimeType.Code.Should().Be("HOM");
        crimeType.DisplayOrder.Should().Be(1);
        crimeType.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Person_ShouldHaveRequiredProperties()
    {
        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "Jane",
            LastName = "Smith",
            DateOfBirth = new DateTime(1990, 1, 1),
            Gender = "Female",
            NationalIdNumber = "NID123456",
            Email = "jane.smith@example.com",
            IsActive = true,
            CreatedById = Guid.NewGuid()
        };

        person.Id.Should().NotBeEmpty();
        person.FirstName.Should().Be("Jane");
        person.LastName.Should().Be("Smith");
        person.FullName.Should().Be("Jane Smith");
        person.NationalIdNumber.Should().Be("NID123456");
        person.IsActive.Should().BeTrue();
    }

    [Fact]
    public void PersonAlias_ShouldHaveRequiredProperties()
    {
        var alias = new PersonAlias
        {
            Id = Guid.NewGuid(),
            PersonId = Guid.NewGuid(),
            FirstName = "J",
            LastName = "Doe",
            AliasType = "Nickname",
            IsPrimaryAlias = false,
            CreatedById = Guid.NewGuid()
        };

        alias.Id.Should().NotBeEmpty();
        alias.FullName.Should().Be("J Doe");
        alias.AliasType.Should().Be("Nickname");
        alias.IsPrimaryAlias.Should().BeFalse();
    }

    [Fact]
    public void Location_ShouldHaveRequiredProperties()
    {
        var location = new Location
        {
            Id = Guid.NewGuid(),
            Name = "123 Main St",
            Type = "Address",
            Address = "123 Main St, City, State",
            Latitude = 40.7128,
            Longitude = -74.0060,
            PostalCode = "10001",
            IsActive = true
        };

        location.Id.Should().NotBeEmpty();
        location.Name.Should().Be("123 Main St");
        location.Latitude.Should().Be(40.7128);
        location.Longitude.Should().Be(-74.0060);
    }

    [Fact]
    public void District_ShouldHaveRequiredProperties()
    {
        var district = new District
        {
            Id = Guid.NewGuid(),
            Name = "Central District",
            Code = "CD",
            Description = "Central business district",
            IsActive = true
        };

        district.Id.Should().NotBeEmpty();
        district.Name.Should().Be("Central District");
        district.Code.Should().Be("CD");
    }

    [Fact]
    public void PoliceStation_ShouldHaveRequiredProperties()
    {
        var station = new PoliceStation
        {
            Id = Guid.NewGuid(),
            Name = "Central HQ",
            Code = "CHQ",
            Address = "100 Police Plaza",
            PhoneNumber = "555-0100",
            Email = "chq@police.gov",
            DistrictId = Guid.NewGuid(),
            IsActive = true
        };

        station.Id.Should().NotBeEmpty();
        station.Name.Should().Be("Central HQ");
        station.Code.Should().Be("CHQ");
    }
}