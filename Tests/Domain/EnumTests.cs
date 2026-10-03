using NETRA.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace NETRA.Tests.Domain;

public class EnumTests
{
    [Fact]
    public void CaseStatus_ShouldHaveExpectedValues()
    {
        Enum.GetValues<CaseStatus>().Should().HaveCount(6);
        CaseStatus.Open.Should().Be(CaseStatus.Open);
        CaseStatus.UnderInvestigation.Should().Be((CaseStatus)2);
        CaseStatus.Closed.Should().Be((CaseStatus)4);
    }

    [Fact]
    public void CaseSeverity_ShouldHaveExpectedValues()
    {
        Enum.GetValues<CaseSeverity>().Should().HaveCount(4);
        CaseSeverity.Low.Should().Be(CaseSeverity.Low);
        CaseSeverity.Critical.Should().Be((CaseSeverity)4);
    }

    [Fact]
    public void PersonRole_ShouldHaveExpectedValues()
    {
        Enum.GetValues<PersonRole>().Should().HaveCount(8);
        PersonRole.Suspect.Should().Be(PersonRole.Suspect);
        PersonRole.Victim.Should().Be((PersonRole)2);
        PersonRole.Investigator.Should().Be((PersonRole)5);
    }

    [Fact]
    public void AlertSeverity_ShouldHaveExpectedValues()
    {
        Enum.GetValues<AlertSeverity>().Should().HaveCount(5);
        AlertSeverity.Info.Should().Be(AlertSeverity.Info);
        AlertSeverity.Critical.Should().Be((AlertSeverity)5);
    }

    [Fact]
    public void AlertStatus_ShouldHaveExpectedValues()
    {
        Enum.GetValues<AlertStatus>().Should().HaveCount(6);
        AlertStatus.New.Should().Be(AlertStatus.New);
        AlertStatus.Resolved.Should().Be((AlertStatus)4);
    }

    [Fact]
    public void RelationshipType_ShouldHaveExpectedValues()
    {
        Enum.GetValues<RelationshipType>().Should().HaveCount(9);
        RelationshipType.Associate.Should().Be(RelationshipType.Associate);
        RelationshipType.Family.Should().Be((RelationshipType)2);
    }

    [Fact]
    public void ReportType_ShouldHaveExpectedValues()
    {
        Enum.GetValues<ReportType>().Should().HaveCount(8);
        ReportType.CaseSummary.Should().Be(ReportType.CaseSummary);
        ReportType.Custom.Should().Be((ReportType)8);
    }

    [Fact]
    public void ImportStatus_ShouldHaveExpectedValues()
    {
        Enum.GetValues<ImportStatus>().Should().HaveCount(7);
        ImportStatus.Pending.Should().Be(ImportStatus.Pending);
        ImportStatus.Completed.Should().Be((ImportStatus)3);
        ImportStatus.Validated.Should().Be((ImportStatus)7);
    }

    [Fact]
    public void ImportType_ShouldCoverTheOnlySupportedSchema()
    {
        Enum.GetValues<ImportType>().Should().HaveCount(1);
        ImportType.CrimeCases.Should().Be((ImportType)1);
    }

    [Fact]
    public void ImportRecordStatus_ShouldHaveExpectedValues()
    {
        Enum.GetValues<ImportRecordStatus>().Should().HaveCount(5);
        ImportRecordStatus.Pending.Should().Be((ImportRecordStatus)1);
        ImportRecordStatus.Duplicate.Should().Be((ImportRecordStatus)4);
        ImportRecordStatus.Imported.Should().Be((ImportRecordStatus)5);
    }

    [Fact]
    public void ImportIssueSeverity_ShouldHaveExpectedValues()
    {
        Enum.GetValues<ImportIssueSeverity>().Should().HaveCount(2);
        ImportIssueSeverity.Error.Should().Be((ImportIssueSeverity)1);
        ImportIssueSeverity.Warning.Should().Be((ImportIssueSeverity)2);
    }

    [Fact]
    public void AnomalySeverity_ShouldHaveExpectedValues()
    {
        Enum.GetValues<AnomalySeverity>().Should().HaveCount(4);
        AnomalySeverity.Low.Should().Be(AnomalySeverity.Low);
        AnomalySeverity.Critical.Should().Be((AnomalySeverity)4);
    }
}