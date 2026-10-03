using Microsoft.EntityFrameworkCore;
using NETRA.Application.Analytics;
using NETRA.Domain.Cases;
using NETRA.Domain.Enums;
using NETRA.Domain.Geography;
using NETRA.Domain.Security;
using NETRA.Infrastructure.Data;
using NETRA.Infrastructure.Services;

namespace NETRA.Tests.Application;

/// <summary>
/// Service-level tests for <see cref="AnalyticsService"/> against the EF Core
/// in-memory provider. Verifies aggregation, filters, empty data and lookups.
/// </summary>
public sealed class AnalyticsServiceTests : IDisposable
{
    private static readonly DateTime FixedToday = new(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);

    private readonly NetraDbContext _context;
    private readonly AnalyticsService _service;
    private readonly Guid _actorId = Guid.NewGuid();
    private Guid _theftId;
    private Guid _assaultId;
    private Guid _centralId;
    private Guid _northId;

    public AnalyticsServiceTests()
    {
        _context = new NetraDbContext(
            new DbContextOptionsBuilder<NetraDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        _service = new AnalyticsService(_context, new FixedTimeProvider(FixedToday));
    }

    public void Dispose() => _context.Dispose();

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private async Task SeedReferenceDataAsync()
    {
        _context.Users.Add(new ApplicationUser
        {
            Id = _actorId,
            UserName = "seed@netra.local",
            Email = "seed@netra.local",
            FirstName = "Seed",
            LastName = "User",
            EmailConfirmed = true,
            IsActive = true
        });

        var theft = new CrimeType { Name = "Theft", Code = "THF", DisplayOrder = 1, IsActive = true };
        var assault = new CrimeType { Name = "Assault", Code = "ASL", DisplayOrder = 2, IsActive = true };
        var fraud = new CrimeType { Name = "Fraud", Code = "FRD", DisplayOrder = 3, IsActive = false };
        _context.CrimeTypes.AddRange(theft, assault, fraud);

        var central = new District { Name = "Central", Code = "CD", IsActive = true };
        var north = new District { Name = "North", Code = "ND", IsActive = true };
        var west = new District { Name = "West", Code = "WD", IsActive = false };
        _context.Districts.AddRange(central, north, west);

        await _context.SaveChangesAsync();

        _theftId = theft.Id;
        _assaultId = assault.Id;
        _centralId = central.Id;
        _northId = north.Id;
    }

    private async Task<CrimeCase> AddCaseAsync(
        DateTime incidentDate,
        CaseSeverity severity,
        CaseStatus status,
        Guid crimeTypeId,
        Guid districtId)
    {
        var crimeCase = new CrimeCase
        {
            CaseNumber = $"CC-{Guid.NewGuid():N}"[..14],
            Title = "Analytics seed case",
            CrimeTypeId = crimeTypeId,
            Status = status,
            Severity = severity,
            IncidentDate = incidentDate,
            CreatedById = _actorId,
            CreatedAt = incidentDate
        };
        _context.CrimeCases.Add(crimeCase);

        var location = new Location
        {
            Name = "Scene",
            DistrictId = districtId,
            CreatedAt = incidentDate
        };
        _context.Locations.Add(location);
        _context.CaseLocations.Add(new CaseLocation
        {
            CrimeCaseId = crimeCase.Id,
            LocationId = location.Id,
            LocationRole = CaseService.IncidentLocationRole,
            AssociatedAt = incidentDate
        });

        await _context.SaveChangesAsync();
        return crimeCase;
    }

    /// <summary>
    /// Four cases inside the default 12-month window (2025-07-01 .. 2026-06-15)
    /// plus one case before the window.
    /// </summary>
    private async Task SeedCasesAsync()
    {
        await AddCaseAsync(new DateTime(2026, 1, 5, 3, 0, 0, DateTimeKind.Utc),
            CaseSeverity.High, CaseStatus.Open, _theftId, _centralId);
        await AddCaseAsync(new DateTime(2026, 2, 10, 9, 0, 0, DateTimeKind.Utc),
            CaseSeverity.Low, CaseStatus.Closed, _assaultId, _northId);
        await AddCaseAsync(new DateTime(2026, 2, 20, 21, 0, 0, DateTimeKind.Utc),
            CaseSeverity.Critical, CaseStatus.Open, _theftId, _centralId);
        await AddCaseAsync(new DateTime(2026, 3, 3, 14, 0, 0, DateTimeKind.Utc),
            CaseSeverity.Low, CaseStatus.Closed, _theftId, _northId);
        await AddCaseAsync(new DateTime(2025, 6, 20, 8, 0, 0, DateTimeKind.Utc),
            CaseSeverity.High, CaseStatus.Archived, _theftId, _centralId);
    }

    // ------------------------------------------------------- window

    [Fact]
    public async Task GetReportAsync_WithoutFilters_UsesTwelveMonthWindowEndingToday()
    {
        await SeedReferenceDataAsync();

        var report = await _service.GetReportAsync(new AnalyticsFilterCriteria());

        report.DateFrom.Should().Be(new DateTime(2025, 7, 1));
        report.DateTo.Should().Be(new DateTime(2026, 6, 15));
        report.MonthlyTrend.Should().HaveCount(12);
    }

    [Fact]
    public async Task GetReportAsync_RejectsInvertedDateRange()
    {
        await SeedReferenceDataAsync();

        var act = () => _service.GetReportAsync(new AnalyticsFilterCriteria
        {
            DateFrom = new DateTime(2026, 5, 10),
            DateTo = new DateTime(2026, 5, 1)
        });

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ------------------------------------------------------- empty data

    [Fact]
    public async Task GetReportAsync_WithNoCases_ReturnsZeroedReport()
    {
        await SeedReferenceDataAsync();

        var report = await _service.GetReportAsync(new AnalyticsFilterCriteria
        {
            DateFrom = new DateTime(2026, 1, 1),
            DateTo = FixedToday
        });

        report.TotalCrimeVolume.Should().Be(0);
        report.HighSeverityCount.Should().Be(0);
        report.ClearanceRate.Should().Be(0);
        report.AverageDailyVolume.Should().Be(0);
        report.MonthlyTrend.Should().OnlyContain(p => p.Count == 0);
        report.CrimeByType.Should().BeEmpty();
        report.CrimeByDistrict.Should().BeEmpty();
        report.SeverityDistribution.Should().OnlyContain(c => c.Count == 0);
        report.Highlights.LeadingCrimeType.Should().BeNull();
        report.Highlights.HighestVolumeDistrict.Should().BeNull();
    }

    // ------------------------------------------------------- aggregation

    [Fact]
    public async Task GetReportAsync_CountsVolumesAndRates()
    {
        await SeedReferenceDataAsync();
        await SeedCasesAsync();

        var report = await _service.GetReportAsync(new AnalyticsFilterCriteria());

        report.TotalCrimeVolume.Should().Be(4);
        report.HighSeverityCount.Should().Be(2);
        report.ClearanceRate.Should().Be(50.0m);
    }

    [Fact]
    public async Task GetReportAsync_MonthlyTrend_ZeroFillsMonthsWithoutCases()
    {
        await SeedReferenceDataAsync();
        await SeedCasesAsync();

        var report = await _service.GetReportAsync(new AnalyticsFilterCriteria());

        report.MonthlyTrend.Select(p => p.Count).Should().Equal(0, 0, 0, 0, 0, 0, 1, 2, 1, 0, 0, 0);
    }

    [Fact]
    public async Task GetReportAsync_CrimeByType_OrdersByVolumeThenName()
    {
        await SeedReferenceDataAsync();
        await SeedCasesAsync();

        var report = await _service.GetReportAsync(new AnalyticsFilterCriteria());

        report.CrimeByType.Select(t => (t.Label, t.Count))
            .Should().Equal(("Theft", 3), ("Assault", 1));
    }

    [Fact]
    public async Task GetReportAsync_SeverityDistribution_AlwaysIncludesAllSeverities()
    {
        await SeedReferenceDataAsync();
        await SeedCasesAsync();

        var report = await _service.GetReportAsync(new AnalyticsFilterCriteria());

        report.SeverityDistribution.Select(s => (s.Label, s.Count))
            .Should().Equal(("Low", 2), ("Medium", 0), ("High", 1), ("Critical", 1));
    }

    [Fact]
    public async Task GetReportAsync_DistrictComparison_SplitsCountsBySeverity()
    {
        await SeedReferenceDataAsync();
        await SeedCasesAsync();

        var report = await _service.GetReportAsync(new AnalyticsFilterCriteria());

        var central = report.CrimeByDistrict.Single(d => d.DistrictName == "Central");
        central.High.Should().Be(1);
        central.Critical.Should().Be(1);
        central.Total.Should().Be(2);

        var north = report.CrimeByDistrict.Single(d => d.DistrictName == "North");
        north.Low.Should().Be(2);
        north.Total.Should().Be(2);
    }

    [Fact]
    public async Task GetReportAsync_Patterns_AreDerivedFromStoredIncidentTimes()
    {
        await SeedReferenceDataAsync();
        await SeedCasesAsync();

        var report = await _service.GetReportAsync(new AnalyticsFilterCriteria
        {
            DateFrom = new DateTime(2026, 1, 1),
            DateTo = FixedToday
        });

        report.DayOfWeekPattern.Single(d => d.Label == "Mon").Count.Should().Be(1);
        report.DayOfWeekPattern.Single(d => d.Label == "Tue").Count.Should().Be(2);
        report.DayOfWeekPattern.Single(d => d.Label == "Fri").Count.Should().Be(1);

        report.TimeOfDayPattern.Select(t => (t.Label, t.Count))
            .Should().Equal(("00–05", 1), ("06–11", 1), ("12–17", 1), ("18–23", 1));
    }

    [Fact]
    public async Task GetReportAsync_Highlights_AreTakenFromTheAggregates()
    {
        await SeedReferenceDataAsync();
        await SeedCasesAsync();

        var report = await _service.GetReportAsync(new AnalyticsFilterCriteria
        {
            DateFrom = new DateTime(2026, 1, 1),
            DateTo = FixedToday
        });

        report.Highlights.LeadingCrimeType.Should().Be("Theft");
        report.Highlights.LeadingCrimeTypeCount.Should().Be(3);
        report.Highlights.PeakMonthCount.Should().Be(2);
        report.Highlights.PeakDayOfWeek.Should().Be("Tue");
        report.Highlights.HighestVolumeDistrict.Should().Be("Central");
    }

    // ------------------------------------------------------- filters

    [Fact]
    public async Task GetReportAsync_FiltersByDateRange()
    {
        await SeedReferenceDataAsync();
        await SeedCasesAsync();

        var report = await _service.GetReportAsync(new AnalyticsFilterCriteria
        {
            DateFrom = new DateTime(2026, 2, 1),
            DateTo = new DateTime(2026, 2, 28)
        });

        report.TotalCrimeVolume.Should().Be(2);
        report.MonthlyTrend.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetReportAsync_FiltersByDistrict()
    {
        await SeedReferenceDataAsync();
        await SeedCasesAsync();

        var report = await _service.GetReportAsync(new AnalyticsFilterCriteria
        {
            DistrictId = _northId
        });

        report.TotalCrimeVolume.Should().Be(2);
        report.CrimeByDistrict.Should().OnlyContain(d => d.DistrictName == "North");
    }

    [Fact]
    public async Task GetReportAsync_FiltersByCrimeType()
    {
        await SeedReferenceDataAsync();
        await SeedCasesAsync();

        var report = await _service.GetReportAsync(new AnalyticsFilterCriteria
        {
            CrimeTypeId = _assaultId
        });

        report.TotalCrimeVolume.Should().Be(1);
        report.CrimeByType.Should().ContainSingle(t => t.Label == "Assault");
    }

    [Fact]
    public async Task GetReportAsync_FiltersBySeverity_CaseInsensitive()
    {
        await SeedReferenceDataAsync();
        await SeedCasesAsync();

        var report = await _service.GetReportAsync(new AnalyticsFilterCriteria
        {
            Severity = "high"
        });

        // In-window High case only; the archived High case sits before the window.
        report.TotalCrimeVolume.Should().Be(1);
    }

    [Fact]
    public async Task GetReportAsync_IgnoresUnparsableSeverityInsteadOfThrowing()
    {
        await SeedReferenceDataAsync();
        await SeedCasesAsync();

        var report = await _service.GetReportAsync(new AnalyticsFilterCriteria
        {
            Severity = "NotASeverity"
        });

        report.TotalCrimeVolume.Should().Be(4);
    }

    [Fact]
    public async Task GetReportAsync_CombinesFilters()
    {
        await SeedReferenceDataAsync();
        await SeedCasesAsync();

        var report = await _service.GetReportAsync(new AnalyticsFilterCriteria
        {
            DistrictId = _centralId,
            CrimeTypeId = _theftId,
            Severity = "Critical"
        });

        report.TotalCrimeVolume.Should().Be(1);
    }

    // ------------------------------------------------------- lookups

    [Fact]
    public async Task GetLookupsAsync_ReturnsOnlyActiveSortedEntries()
    {
        await SeedReferenceDataAsync();

        var lookups = await _service.GetLookupsAsync();

        lookups.Districts.Select(d => d.Name).Should().Equal("Central", "North");
        lookups.CrimeTypes.Select(c => c.Name).Should().Equal("Theft", "Assault");
    }
}
