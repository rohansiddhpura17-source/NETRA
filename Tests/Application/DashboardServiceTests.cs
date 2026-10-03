using Microsoft.EntityFrameworkCore;
using NETRA.Application.Analytics;
using NETRA.Application.Dashboard;
using NETRA.Domain.Alerts;
using NETRA.Domain.Cases;
using NETRA.Domain.Enums;
using NETRA.Domain.Geography;
using NETRA.Domain.Intelligence;
using NETRA.Domain.Security;
using NETRA.Infrastructure.Data;
using NETRA.Infrastructure.Services;

namespace NETRA.Tests.Application;

/// <summary>
/// Service-level tests for <see cref="DashboardService"/>: KPI counts, high-risk
/// districts from recorded RiskAssessments, recent alerts, and honest empty states.
/// </summary>
public sealed class DashboardServiceTests : IDisposable
{
    private static readonly DateTime FixedToday = new(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);

    private readonly NetraDbContext _context;
    private readonly DashboardService _service;
    private readonly Guid _actorId = Guid.NewGuid();
    private Guid _theftId;
    private Guid _centralId;
    private Guid _northId;

    public DashboardServiceTests()
    {
        _context = new NetraDbContext(
            new DbContextOptionsBuilder<NetraDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        var time = new FixedTimeProvider(FixedToday);
        _service = new DashboardService(_context, new AnalyticsService(_context, time), time);
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
        _context.CrimeTypes.Add(theft);

        var central = new District { Name = "Central", Code = "CD", IsActive = true };
        var north = new District { Name = "North", Code = "ND", IsActive = true };
        _context.Districts.AddRange(central, north);

        await _context.SaveChangesAsync();

        _theftId = theft.Id;
        _centralId = central.Id;
        _northId = north.Id;
    }

    private async Task<CrimeCase> AddCaseAsync(
        string caseNumber,
        CaseStatus status,
        DateTime incidentDate,
        Guid districtId)
    {
        var crimeCase = new CrimeCase
        {
            CaseNumber = caseNumber,
            Title = $"Case {caseNumber}",
            CrimeTypeId = _theftId,
            Status = status,
            Severity = CaseSeverity.High,
            IncidentDate = incidentDate,
            CreatedById = _actorId,
            CreatedAt = incidentDate
        };
        _context.CrimeCases.Add(crimeCase);

        var location = new Location
        {
            Name = $"Scene {caseNumber}",
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

    private async Task AddAlertAsync(
        string title,
        AlertStatus status,
        AlertSeverity severity,
        DateTime createdAt,
        Guid? caseId = null)
    {
        _context.Alerts.Add(new Alert
        {
            Title = title,
            Status = status,
            Severity = severity,
            AlertType = 1,
            CrimeCaseId = caseId,
            CreatedAt = createdAt
        });
        await _context.SaveChangesAsync();
    }

    private async Task AddRiskAsync(CrimeCase crimeCase, int level, decimal score)
    {
        _context.RiskAssessments.Add(new RiskAssessment
        {
            CrimeCaseId = crimeCase.Id,
            Title = $"Risk for {crimeCase.CaseNumber}",
            RiskLevel = level,
            RiskScore = score,
            AssessedAt = FixedToday,
            AssessedById = _actorId,
            CreatedAt = FixedToday
        });
        await _context.SaveChangesAsync();
    }

    // ------------------------------------------------------- KPI counts

    [Fact]
    public async Task GetDashboardAsync_CountsTotalAndActiveCasesSeparately()
    {
        await SeedReferenceDataAsync();
        await AddCaseAsync("CC-1", CaseStatus.Open, new DateTime(2026, 5, 1), _centralId);
        await AddCaseAsync("CC-2", CaseStatus.Closed, new DateTime(2026, 5, 2), _centralId);
        await AddCaseAsync("CC-3", CaseStatus.UnderInvestigation, new DateTime(2026, 5, 3), _northId);
        await AddCaseAsync("CC-4", CaseStatus.Archived, new DateTime(2026, 5, 4), _northId);

        var dto = await _service.GetDashboardAsync();

        dto.TotalCases.Should().Be(4);
        dto.ActiveCases.Should().Be(2);
    }

    [Fact]
    public async Task GetDashboardAsync_CountsOnlyUnresolvedAlerts()
    {
        await SeedReferenceDataAsync();
        await AddAlertAsync("A1", AlertStatus.New, AlertSeverity.High, FixedToday);
        await AddAlertAsync("A2", AlertStatus.Escalated, AlertSeverity.Critical, FixedToday);
        await AddAlertAsync("A3", AlertStatus.Resolved, AlertSeverity.High, FixedToday);
        await AddAlertAsync("A4", AlertStatus.Dismissed, AlertSeverity.Info, FixedToday);

        var dto = await _service.GetDashboardAsync();

        dto.ActiveAlerts.Should().Be(2);
    }

    // ------------------------------------------------------- high-risk areas

    [Fact]
    public async Task GetDashboardAsync_HighRiskAreas_IncludeOnlyAssessmentsAtOrAboveThreshold()
    {
        await SeedReferenceDataAsync();
        var centralCase = await AddCaseAsync("CC-1", CaseStatus.Open, new DateTime(2026, 5, 1), _centralId);
        var northCase = await AddCaseAsync("CC-2", CaseStatus.Open, new DateTime(2026, 5, 2), _northId);

        await AddRiskAsync(centralCase, level: 4, score: 8.5m);
        await AddRiskAsync(northCase, level: 2, score: 5.0m);

        var dto = await _service.GetDashboardAsync();

        dto.HighRiskAreaCount.Should().Be(1);
        dto.HighRiskAreas.Should().ContainSingle()
            .Which.DistrictName.Should().Be("Central");
    }

    [Fact]
    public async Task GetDashboardAsync_HighRiskAreas_AggregatePerDistrictAndRankByScore()
    {
        await SeedReferenceDataAsync();
        var centralA = await AddCaseAsync("CC-1", CaseStatus.Open, new DateTime(2026, 5, 1), _centralId);
        var centralB = await AddCaseAsync("CC-2", CaseStatus.Open, new DateTime(2026, 5, 2), _centralId);
        var north = await AddCaseAsync("CC-3", CaseStatus.Open, new DateTime(2026, 5, 3), _northId);

        await AddRiskAsync(centralA, level: 5, score: 9.2m);
        await AddRiskAsync(centralB, level: 3, score: 7.1m);
        await AddRiskAsync(north, level: 3, score: 6.4m);

        var dto = await _service.GetDashboardAsync();

        dto.HighRiskAreaCount.Should().Be(2);
        dto.HighRiskAreas[0].DistrictName.Should().Be("Central");
        dto.HighRiskAreas[0].AssessedCaseCount.Should().Be(2);
        dto.HighRiskAreas[0].MaximumRecordedScore.Should().Be(9.2m);
        dto.HighRiskAreas[0].MaximumRiskLevel.Should().Be(5);
        dto.HighRiskAreas[1].DistrictName.Should().Be("North");
    }

    [Fact]
    public async Task GetDashboardAsync_HighRiskAreas_WithoutAssessmentsIsEmpty()
    {
        await SeedReferenceDataAsync();
        await AddCaseAsync("CC-1", CaseStatus.Open, new DateTime(2026, 5, 1), _centralId);

        var dto = await _service.GetDashboardAsync();

        dto.HighRiskAreaCount.Should().Be(0);
        dto.HighRiskAreas.Should().BeEmpty();
    }

    // ------------------------------------------------------- recent alerts

    [Fact]
    public async Task GetDashboardAsync_RecentAlerts_TakeFiveMostRecentActiveWithCaseNumbers()
    {
        await SeedReferenceDataAsync();
        var crimeCase = await AddCaseAsync("CC-42", CaseStatus.Open, new DateTime(2026, 5, 1), _centralId);

        await AddAlertAsync("Old", AlertStatus.New, AlertSeverity.Info, new DateTime(2026, 5, 1));
        for (var i = 1; i <= 6; i++)
        {
            await AddAlertAsync(
                $"Alert {i}", AlertStatus.New, AlertSeverity.High,
                new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i),
                i == 1 ? crimeCase.Id : null);
        }
        await AddAlertAsync("Resolved", AlertStatus.Resolved, AlertSeverity.Critical, FixedToday);

        var dto = await _service.GetDashboardAsync();

        dto.RecentAlerts.Should().HaveCount(5);
        dto.RecentAlerts.Should().OnlyContain(a => a.Status != nameof(AlertStatus.Resolved));
        dto.RecentAlerts.Select(a => a.Title)
            .Should().ContainInOrder("Alert 6", "Alert 5", "Alert 4", "Alert 3", "Alert 2");
        dto.RecentAlerts.Should().NotContain(a => a.Title == "Old");
        dto.RecentAlerts.Should().NotContain(a => a.Title == "Alert 1");
        dto.RecentAlerts.Should().NotContain(a => a.Title == "Resolved");
    }

    [Fact]
    public async Task GetDashboardAsync_RecentAlerts_IncludeCaseNumberWhenLinked()
    {
        await SeedReferenceDataAsync();
        var crimeCase = await AddCaseAsync("CC-42", CaseStatus.Open, new DateTime(2026, 5, 1), _centralId);
        await AddAlertAsync("Linked", AlertStatus.New, AlertSeverity.High, FixedToday, crimeCase.Id);
        await AddAlertAsync("Unlinked", AlertStatus.New, AlertSeverity.High, FixedToday);

        var dto = await _service.GetDashboardAsync();

        var linked = dto.RecentAlerts.Single(a => a.Title == "Linked");
        linked.CaseNumber.Should().Be("CC-42");
        dto.RecentAlerts.Single(a => a.Title == "Unlinked").CaseNumber.Should().BeNull();
    }

    // ------------------------------------------------------- reuse + empty

    [Fact]
    public async Task GetDashboardAsync_TrendAndDistribution_ReuseAnalyticsWindow()
    {
        await SeedReferenceDataAsync();
        await AddCaseAsync("CC-1", CaseStatus.Open, new DateTime(2026, 5, 1), _centralId);
        await AddCaseAsync("CC-2", CaseStatus.Open, new DateTime(2026, 5, 2), _centralId);

        var dto = await _service.GetDashboardAsync();

        dto.TrendFrom.Should().Be(new DateTime(2025, 7, 1));
        dto.TrendTo.Should().Be(new DateTime(2026, 6, 15));
        dto.CrimeTrend.Should().HaveCount(12);
        dto.CrimeDistribution.Should().ContainSingle(c => c.Label == "Theft" && c.Count == 2);
        dto.DistrictComparison.Should().ContainSingle(d => d.DistrictName == "Central");
    }

    [Fact]
    public async Task GetDashboardAsync_WithNoData_ReturnsZerosAndEmptyLists()
    {
        await SeedReferenceDataAsync();

        var dto = await _service.GetDashboardAsync();

        dto.TotalCases.Should().Be(0);
        dto.ActiveCases.Should().Be(0);
        dto.ActiveAlerts.Should().Be(0);
        dto.HighRiskAreaCount.Should().Be(0);
        dto.CrimeDistribution.Should().BeEmpty();
        dto.DistrictComparison.Should().BeEmpty();
        dto.HighRiskAreas.Should().BeEmpty();
        dto.RecentAlerts.Should().BeEmpty();
    }
}
