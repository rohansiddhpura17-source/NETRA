using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NETRA.Application.Abstractions;
using NETRA.Application.Analytics;
using NETRA.Application.Risk;
using NETRA.Domain.Cases;
using NETRA.Domain.Enums;
using NETRA.Domain.Geography;
using NETRA.Domain.Intelligence;
using NETRA.Domain.Security;
using NETRA.Infrastructure.Data;
using NETRA.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace NETRA.Tests.Application;

/// <summary>
/// Service-level tests for <see cref="RiskAnalysisService"/>. Scores are asserted
/// against the documented additive formula (volume + severity + change +
/// concentration), determinism is verified by repeated runs, and persistence rules
/// (one area assessment per district, no duplicate open anomalies, audit entries)
/// are exercised through <see cref="IRiskAnalysisService.RunAssessmentAsync"/>.
/// </summary>
public sealed class RiskAnalysisServiceTests : IDisposable
{
    // Fixed clock so the default window is deterministic:
    // window 2026-05-17..2026-06-15, comparison 2026-04-17..2026-05-16.
    private static readonly DateTime FixedToday = new(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);

    private readonly NetraDbContext _context;
    private readonly Guid _actorId = Guid.NewGuid();
    private Guid _centralId;
    private Guid _northId;
    private Guid _theftId;
    private Guid _assaultId;

    public RiskAnalysisServiceTests()
    {
        _context = new NetraDbContext(
            new DbContextOptionsBuilder<NetraDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        SeedReferenceData();
    }

    public void Dispose() => _context.Dispose();

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class StubActorAccessor : IAuditActorAccessor
    {
        private readonly Guid _id;
        public StubActorAccessor(Guid id) => _id = id;
        public Guid GetUserId() => _id;
        public Guid GetUserId(System.Security.Claims.ClaimsPrincipal? principal) => _id;
    }

    private RiskAnalysisService CreateService(Guid? actorId = null) =>
        new(
            _context,
            new AnomalyDetector(),
            new FixedTimeProvider(FixedToday),
            new StubActorAccessor(actorId ?? _actorId),
            new AuditService(
                _context,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
                new StubActorAccessor(actorId ?? _actorId)));

    private void SeedReferenceData()
    {
        _context.Users.Add(new ApplicationUser
        {
            Id = _actorId,
            UserName = "risk.seed@netra.local",
            Email = "risk.seed@netra.local",
            FirstName = "Risk",
            LastName = "Seed",
            EmailConfirmed = true,
            IsActive = true
        });

        var theft = new CrimeType { Name = "Theft", Code = "THF", DisplayOrder = 1, IsActive = true };
        var assault = new CrimeType { Name = "Assault", Code = "ASL", DisplayOrder = 2, IsActive = true };
        var central = new District { Name = "Central", Code = "CD", IsActive = true };
        var north = new District { Name = "North", Code = "ND", IsActive = true };
        _context.CrimeTypes.AddRange(theft, assault);
        _context.Districts.AddRange(central, north);
        _context.SaveChanges();

        _theftId = theft.Id;
        _assaultId = assault.Id;
        _centralId = central.Id;
        _northId = north.Id;
    }

    private void AddCase(
        string caseNumber,
        CaseSeverity severity,
        DateTime incidentDate,
        Guid districtId,
        string crimeType = "Theft")
    {
        var crimeCase = new CrimeCase
        {
            CaseNumber = caseNumber,
            Title = $"Case {caseNumber}",
            CrimeTypeId = crimeType == "Theft" ? _theftId : _assaultId,
            Status = CaseStatus.Open,
            Severity = severity,
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

        _context.SaveChanges();
    }

    /// <summary>
    /// The reference dataset used by several tests: Central has five Theft cases in
    /// the window (two High, one Critical) versus two Low cases in the comparison
    /// period; North has one case in each period (sample too small to score).
    /// </summary>
    private void AddReferenceDataset()
    {
        AddCase("CC-R-1", CaseSeverity.High, new DateTime(2026, 5, 20), _centralId);
        AddCase("CC-R-2", CaseSeverity.Medium, new DateTime(2026, 5, 21), _centralId);
        AddCase("CC-R-3", CaseSeverity.High, new DateTime(2026, 6, 10), _centralId);
        AddCase("CC-R-4", CaseSeverity.Critical, new DateTime(2026, 6, 11), _centralId);
        AddCase("CC-R-5", CaseSeverity.Medium, new DateTime(2026, 6, 12), _centralId);
        AddCase("CC-R-6", CaseSeverity.Low, new DateTime(2026, 5, 1), _centralId);
        AddCase("CC-R-7", CaseSeverity.Low, new DateTime(2026, 5, 10), _centralId);
        AddCase("CC-R-8", CaseSeverity.Medium, new DateTime(2026, 6, 5), _northId);
        AddCase("CC-R-9", CaseSeverity.Medium, new DateTime(2026, 4, 20), _northId);
    }

    private static AnalyticsFilterCriteria NoFilter() => new();

    // ------------------------------------------------------------ window arithmetic

    [Fact]
    public async Task GetAnalysisAsync_DefaultWindow_UsesTrailingThirtyDaysAndPrecedingComparison()
    {
        var service = CreateService();

        var analysis = await service.GetAnalysisAsync(NoFilter());

        analysis.DateFrom.Should().Be(new DateTime(2026, 5, 17));
        analysis.DateTo.Should().Be(new DateTime(2026, 6, 15));
        analysis.ComparisonFrom.Should().Be(new DateTime(2026, 4, 17));
        analysis.ComparisonTo.Should().Be(new DateTime(2026, 5, 16));
    }

    [Fact]
    public async Task GetAnalysisAsync_ExplicitWindow_ComparisonIsEqualLengthAndImmediatelyPreceding()
    {
        var service = CreateService();

        var analysis = await service.GetAnalysisAsync(new AnalyticsFilterCriteria
        {
            DateFrom = new DateTime(2026, 3, 1),
            DateTo = new DateTime(2026, 3, 15)
        });

        analysis.ComparisonFrom.Should().Be(new DateTime(2026, 2, 14));
        analysis.ComparisonTo.Should().Be(new DateTime(2026, 2, 28));
    }

    [Fact]
    public async Task GetAnalysisAsync_InvertedDates_IsRejected()
    {
        var service = CreateService();

        var act = () => service.GetAnalysisAsync(new AnalyticsFilterCriteria
        {
            DateFrom = new DateTime(2026, 6, 10),
            DateTo = new DateTime(2026, 6, 1)
        });

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*on or before*");
    }

    // ------------------------------------------------------------ documented scoring

    [Fact]
    public async Task GetAnalysisAsync_ReferenceDataset_ScoresMatchTheDocumentedFormula()
    {
        // Central: volume min(40, 5*4)=20; severity 2*3+1*6=12;
        // change: growth (5-2)/2 = 150% -> capped 15; concentration 100% -> 10.
        // 20 + 12 + 15 + 10 = 57 -> Elevated (>= 40).
        AddReferenceDataset();
        var service = CreateService();

        var analysis = await service.GetAnalysisAsync(NoFilter());
        var central = analysis.Indicators.Single(i => i.DistrictName == "Central");

        central.CurrentCount.Should().Be(5);
        central.BaselineCount.Should().Be(2);
        central.HighSeverityCount.Should().Be(2);
        central.CriticalSeverityCount.Should().Be(1);
        central.ChangePercent.Should().Be(150.0m);
        central.Score.Should().Be(57.0m);
        central.Level.Should().Be(3);
        central.LevelLabel.Should().Be("Elevated");
        central.LeadingCrimeType.Should().Be("Theft");
        central.LeadingCrimeTypeShare.Should().Be(100.0m);
        central.Explanation.Should().Be(
            "5 cases in the window versus 2 in the comparison period (+150% versus the comparison period); " +
            "2 high and 1 critical severity. Components: volume +20, severity +12, change +15, " +
            "concentration +10. Score 57 of 100.");
    }

    [Fact]
    public async Task GetAnalysisAsync_WeeklySeries_HasOneBucketPerWeekOfTheWindow()
    {
        AddReferenceDataset();
        var service = CreateService();

        var analysis = await service.GetAnalysisAsync(NoFilter());
        var central = analysis.Indicators.Single(i => i.DistrictName == "Central");

        // 30-day window -> 30/7 + 1 = 5 weekly buckets, oldest first.
        central.WeeklyCounts.Should().HaveCount(5);
        central.WeeklyCounts.Sum().Should().Be(5);
        central.WeeklyCounts.Should().StartWith([2, 0, 0, 3, 0]);
    }

    [Fact]
    public async Task GetAnalysisAsync_LowVolumeStableDistrict_IsScoredLow()
    {
        // Low: 3 window versus 3 baseline, Medium severity, leading type 2 of 3:
        // 12 + 0 + 0 + 6.7 = 18.7 -> Low (< 20).
        AddCase("CC-L-1", CaseSeverity.Medium, new DateTime(2026, 5, 20), _centralId, "Theft");
        AddCase("CC-L-2", CaseSeverity.Medium, new DateTime(2026, 5, 21), _centralId, "Theft");
        AddCase("CC-L-3", CaseSeverity.Medium, new DateTime(2026, 6, 10), _centralId, "Assault");
        AddCase("CC-L-4", CaseSeverity.Medium, new DateTime(2026, 5, 1), _centralId, "Theft");
        AddCase("CC-L-5", CaseSeverity.Medium, new DateTime(2026, 5, 2), _centralId, "Assault");
        AddCase("CC-L-6", CaseSeverity.Medium, new DateTime(2026, 5, 3), _centralId, "Theft");
        var service = CreateService();

        var analysis = await service.GetAnalysisAsync(NoFilter());
        var central = analysis.Indicators.Single(i => i.DistrictName == "Central");

        central.Score.Should().Be(18.7m);
        central.Level.Should().Be(1);
        central.LevelLabel.Should().Be("Low");
        central.ChangePercent.Should().Be(0.0m);
    }

    [Fact]
    public async Task GetAnalysisAsync_ModerateBand_ScoresAsModerate()
    {
        // 4 window versus 4 baseline, one High, leading type 3 of 4:
        // 16 + 3 + 0 + 7.5 = 26.5 -> Moderate (>= 20).
        AddCase("CC-M-1", CaseSeverity.High, new DateTime(2026, 5, 20), _centralId, "Theft");
        AddCase("CC-M-2", CaseSeverity.Medium, new DateTime(2026, 5, 21), _centralId, "Theft");
        AddCase("CC-M-3", CaseSeverity.Medium, new DateTime(2026, 6, 10), _centralId, "Theft");
        AddCase("CC-M-4", CaseSeverity.Medium, new DateTime(2026, 6, 11), _centralId, "Assault");
        for (var day = 1; day <= 4; day++)
        {
            AddCase($"CC-M-b{day}", CaseSeverity.Medium, new DateTime(2026, 5, day), _centralId, "Theft");
        }
        var service = CreateService();

        var analysis = await service.GetAnalysisAsync(NoFilter());
        var central = analysis.Indicators.Single(i => i.DistrictName == "Central");

        central.Score.Should().Be(26.5m);
        central.Level.Should().Be(2);
        central.LevelLabel.Should().Be("Moderate");
    }

    [Fact]
    public async Task GetAnalysisAsync_HighBand_ScoresAsHigh()
    {
        // 9 window (2 High, 1 Critical) versus 4 baseline:
        // 36 + 12 + 15 + 10 = 73 -> High (>= 60).
        AddCase("CC-H-1", CaseSeverity.High, new DateTime(2026, 6, 1), _centralId);
        AddCase("CC-H-2", CaseSeverity.High, new DateTime(2026, 6, 2), _centralId);
        AddCase("CC-H-3", CaseSeverity.Critical, new DateTime(2026, 6, 3), _centralId);
        for (var day = 4; day <= 9; day++)
        {
            AddCase($"CC-H-{day}", CaseSeverity.Medium, new DateTime(2026, 6, day), _centralId);
        }
        for (var day = 1; day <= 4; day++)
        {
            AddCase($"CC-H-b{day}", CaseSeverity.Medium, new DateTime(2026, 4, 20 + day), _centralId);
        }
        var service = CreateService();

        var analysis = await service.GetAnalysisAsync(NoFilter());
        var central = analysis.Indicators.Single(i => i.DistrictName == "Central");

        central.Score.Should().Be(73.0m);
        central.Level.Should().Be(4);
        central.LevelLabel.Should().Be("High");
    }

    [Fact]
    public async Task GetAnalysisAsync_CriticalBand_ScoresAsCritical()
    {
        // 10 window (6 High) versus 5 baseline:
        // 40 + 18 + 15 + 10 = 83 -> Critical (>= 80).
        for (var day = 1; day <= 10; day++)
        {
            AddCase($"CC-C-{day}",
                day <= 6 ? CaseSeverity.High : CaseSeverity.Medium,
                new DateTime(2026, 6, day), _centralId);
        }
        for (var day = 1; day <= 5; day++)
        {
            AddCase($"CC-C-b{day}", CaseSeverity.Medium, new DateTime(2026, 5, 2 + day), _centralId);
        }
        var service = CreateService();

        var analysis = await service.GetAnalysisAsync(NoFilter());
        var central = analysis.Indicators.Single(i => i.DistrictName == "Central");

        central.Score.Should().Be(83.0m);
        central.Level.Should().Be(5);
        central.LevelLabel.Should().Be("Critical");
    }

    // ------------------------------------------------------------ honest empty / small samples

    [Fact]
    public async Task GetAnalysisAsync_BelowMinimumSample_IsReportedAsInsufficientAndNeverScored()
    {
        AddReferenceDataset();
        var service = CreateService();

        var analysis = await service.GetAnalysisAsync(NoFilter());
        var north = analysis.Indicators.Single(i => i.DistrictName == "North");

        north.InsufficientData.Should().BeTrue();
        north.Score.Should().BeNull("a score would be invented rather than explained");
        north.Level.Should().Be(0);
        north.LevelLabel.Should().Be("Insufficient data");
        north.Explanation.Should().Contain("below the 3-case minimum");
        analysis.Anomalies.Should().NotContain(a => a.DistrictName == "North");
    }

    [Fact]
    public async Task GetAnalysisAsync_NoCases_ReturnsEmptyIndicators()
    {
        var service = CreateService();

        var analysis = await service.GetAnalysisAsync(NoFilter());

        analysis.Indicators.Should().BeEmpty();
        analysis.Anomalies.Should().BeEmpty();
        analysis.TotalCasesInWindow.Should().Be(0);
        analysis.LastRecordedRunAt.Should().BeNull();
    }

    [Fact]
    public async Task GetAnalysisAsync_NewActivityWithoutBaseline_UsesFullChangeComponentAndExplainsIt()
    {
        // 3 window, 0 baseline -> sample exactly at the minimum.
        // 12 + 0 + 15 + 10 = 37 -> Moderate; no percentage exists.
        AddCase("CC-N-1", CaseSeverity.Medium, new DateTime(2026, 6, 1), _centralId);
        AddCase("CC-N-2", CaseSeverity.Medium, new DateTime(2026, 6, 2), _centralId);
        AddCase("CC-N-3", CaseSeverity.Medium, new DateTime(2026, 6, 3), _centralId);
        var service = CreateService();

        var analysis = await service.GetAnalysisAsync(NoFilter());
        var central = analysis.Indicators.Single();

        central.ChangePercent.Should().BeNull();
        central.Score.Should().Be(37.0m);
        central.Explanation.Should().Contain("no comparison-period activity");
    }

    [Fact]
    public async Task GetAnalysisAsync_DecreaseVersusBaseline_ReportsNegativeChangeAndNoChangePoints()
    {
        // 2 window versus 4 baseline: growth -50% earns 0 change points.
        AddCase("CC-D-1", CaseSeverity.Medium, new DateTime(2026, 6, 1), _centralId);
        AddCase("CC-D-2", CaseSeverity.Medium, new DateTime(2026, 6, 2), _centralId);
        for (var day = 1; day <= 4; day++)
        {
            AddCase($"CC-D-b{day}", CaseSeverity.Medium, new DateTime(2026, 5, day), _centralId);
        }
        var service = CreateService();

        var analysis = await service.GetAnalysisAsync(NoFilter());
        var central = analysis.Indicators.Single();

        central.ChangePercent.Should().Be(-50.0m);
        // volume 8 + concentration 10 = 18.
        central.Score.Should().Be(18.0m);
    }

    // ------------------------------------------------------------ filtering

    [Fact]
    public async Task GetAnalysisAsync_DistrictFilter_LimitsIndicatorsAndWindowTotals()
    {
        AddReferenceDataset();
        var service = CreateService();

        var analysis = await service.GetAnalysisAsync(new AnalyticsFilterCriteria { DistrictId = _centralId });

        analysis.Indicators.Should().OnlyContain(i => i.DistrictName == "Central");
        analysis.TotalCasesInWindow.Should().Be(5);
    }

    [Fact]
    public async Task GetAnalysisAsync_SeverityFilter_CountsOnlyMatchingSeverity()
    {
        AddReferenceDataset();
        var service = CreateService();

        var analysis = await service.GetAnalysisAsync(new AnalyticsFilterCriteria { Severity = "High" });

        var central = analysis.Indicators.Single(i => i.DistrictName == "Central");
        central.CurrentCount.Should().Be(2);
        central.BaselineCount.Should().Be(0);
        analysis.Indicators.Should().NotContain(i => i.DistrictName == "North");
    }

    [Fact]
    public async Task GetAnalysisAsync_CrimeTypeFilter_CountsOnlyMatchingType()
    {
        AddCase("CC-T-1", CaseSeverity.Medium, new DateTime(2026, 6, 1), _centralId, "Theft");
        AddCase("CC-T-2", CaseSeverity.Medium, new DateTime(2026, 6, 2), _centralId, "Theft");
        AddCase("CC-T-3", CaseSeverity.Medium, new DateTime(2026, 6, 3), _centralId, "Theft");
        AddCase("CC-T-4", CaseSeverity.Medium, new DateTime(2026, 6, 4), _centralId, "Assault");
        AddCase("CC-T-5", CaseSeverity.Medium, new DateTime(2026, 6, 5), _centralId, "Assault");
        var service = CreateService();

        var analysis = await service.GetAnalysisAsync(
            new AnalyticsFilterCriteria { CrimeTypeId = _theftId });

        var central = analysis.Indicators.Single();
        central.CurrentCount.Should().Be(3);
        central.LeadingCrimeType.Should().Be("Theft");
    }

    [Fact]
    public async Task GetAnalysisAsync_CaseWithoutIncidentLocation_IsIgnored()
    {
        AddCase("CC-X-1", CaseSeverity.Medium, new DateTime(2026, 6, 1), _centralId);
        var crimeCase = await _context.CrimeCases.SingleAsync(c => c.CaseNumber == "CC-X-1");
        var stray = await _context.CaseLocations.SingleAsync(cl => cl.CrimeCaseId == crimeCase.Id);
        stray.LocationRole = "Arrest";
        await _context.SaveChangesAsync();
        var service = CreateService();

        var analysis = await service.GetAnalysisAsync(NoFilter());

        analysis.Indicators.Should().BeEmpty();
        analysis.TotalCasesInWindow.Should().Be(0);
    }

    // ------------------------------------------------------------ anomalies + determinism

    [Fact]
    public async Task GetAnalysisAsync_ReferenceDataset_DetectsExplainedAnomalies()
    {
        AddReferenceDataset();
        var service = CreateService();

        var analysis = await service.GetAnalysisAsync(NoFilter());

        var spike = analysis.Anomalies.Single(a => a.AnomalyType == AnomalyTypes.VolumeSpike);
        spike.Title.Should().Be("Crime volume up 150% in Central");
        spike.Description.Should().Contain("compared with the previous comparison period (5 cases versus 2)");

        analysis.Anomalies.Should().Contain(a =>
            a.AnomalyType == AnomalyTypes.SeverityShift &&
            a.Description.Contains("rose from 0 in the comparison period to 3"));
        analysis.Anomalies.Should().Contain(a =>
            a.AnomalyType == AnomalyTypes.CrimeConcentration &&
            a.Description.Contains("Theft accounts for 100%"));
        // Ordered by severity: the Critical spike leads.
        analysis.Anomalies.First().Severity.Should().Be("Critical");
    }

    [Fact]
    public async Task GetAnalysisAsync_RunTwice_ProducesIdenticalIndicatorsAndAnomalies()
    {
        AddReferenceDataset();
        var service = CreateService();

        var first = await service.GetAnalysisAsync(NoFilter());
        var second = await service.GetAnalysisAsync(NoFilter());

        second.Indicators.Select(i => (i.DistrictName, i.Score, i.LevelLabel))
            .Should().Equal(first.Indicators.Select(i => (i.DistrictName, i.Score, i.LevelLabel)));
        second.Anomalies.Select(a => (a.Title, a.Severity, a.Confidence))
            .Should().Equal(first.Anomalies.Select(a => (a.Title, a.Severity, a.Confidence)));
    }

    [Fact]
    public async Task GetAnalysisAsync_IndicatorsRankedByScoreWithInsufficientDataLast()
    {
        AddReferenceDataset();
        var service = CreateService();

        var analysis = await service.GetAnalysisAsync(NoFilter());

        analysis.Indicators.Select(i => i.DistrictName)
            .Should().ContainInOrder("Central", "North");
    }

    // ------------------------------------------------------------ persisted assessment runs

    [Fact]
    public async Task RunAssessmentAsync_WithoutSignedInUser_IsRejected()
    {
        AddReferenceDataset();
        var service = CreateService(actorId: Guid.Empty);

        var act = () => service.RunAssessmentAsync(NoFilter());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*signed-in user*");
    }

    [Fact]
    public async Task RunAssessmentAsync_PersistsOneExplainedAreaAssessmentPerScoringDistrict()
    {
        AddReferenceDataset();
        var service = CreateService();

        var result = await service.RunAssessmentAsync(NoFilter());

        result.AssessmentsSaved.Should().Be(1); // North is insufficient and never stored.

        var assessments = await _context.RiskAssessments.Where(r => r.DistrictId != null).ToListAsync();
        assessments.Should().ContainSingle();

        var assessment = assessments.Single();
        assessment.DistrictId.Should().Be(_centralId);
        assessment.CrimeCaseId.Should().BeNull();
        assessment.Title.Should().Be("Area risk indicator — Central");
        assessment.RiskScore.Should().Be(57.0m);
        assessment.RiskLevel.Should().Be(3);
        assessment.Factors.Should().Contain("Components: volume +20");
        assessment.Assessment.Should()
            .Contain("17 May 2026 to 15 Jun 2026 compared with 17 Apr 2026 to 16 May 2026")
            .And.Contain("not a prediction of future crime");
        result.AnomaliesSaved.Should().Be(3);
        result.RunAt.Should().Be(FixedToday);
    }

    [Fact]
    public async Task RunAssessmentAsync_WritesAuditEntriesForAssessmentsAndAnomalies()
    {
        AddReferenceDataset();
        var service = CreateService();

        await service.RunAssessmentAsync(NoFilter());

        var auditLogs = await _context.AuditLogs
            .Where(a => a.EntityType == AuditEntityTypeNames.RiskAssessment
                        || a.EntityType == AuditEntityTypeNames.Anomaly)
            .ToListAsync();

        auditLogs.Should().ContainSingle(a =>
            a.EntityType == AuditEntityTypeNames.RiskAssessment && a.Action == AuditActionNames.Created);
        auditLogs.Should().HaveCount(4); // one assessment created + three anomalies created
        auditLogs.Should().OnlyContain(a => a.UserId == _actorId);
    }

    [Fact]
    public async Task RunAssessmentAsync_SecondRun_UpsertsAssessmentAndSkipsOpenAnomalies()
    {
        AddReferenceDataset();
        var service = CreateService();

        var first = await service.RunAssessmentAsync(NoFilter());
        var second = await service.RunAssessmentAsync(NoFilter());

        first.AnomaliesSaved.Should().Be(3);
        second.AnomaliesSaved.Should().Be(0);
        second.AnomaliesSkippedAsOpen.Should().Be(3);

        (await _context.RiskAssessments.CountAsync(r => r.DistrictId != null))
            .Should().Be(1, "re-runs refresh the district's assessment in place");
        (await _context.Anomalies.CountAsync(a => a.RelatedEntityType == "District"))
            .Should().Be(3, "open anomalies are never duplicated");
    }

    [Fact]
    public async Task RunAssessmentAsync_ResolvingAnomalies_LetsALaterRunRecordThemAgain()
    {
        AddReferenceDataset();
        var service = CreateService();

        await service.RunAssessmentAsync(NoFilter());
        foreach (var anomaly in await _context.Anomalies.ToListAsync())
        {
            anomaly.Status = 2; // resolved
        }
        await _context.SaveChangesAsync();

        var third = await service.RunAssessmentAsync(NoFilter());

        third.AnomaliesSaved.Should().Be(3, "resolved anomalies no longer block re-detection");
    }

    [Fact]
    public async Task RunAssessmentAsync_MakesLastRecordedRunVisible()
    {
        AddReferenceDataset();
        var service = CreateService();

        (await service.GetAnalysisAsync(NoFilter())).LastRecordedRunAt.Should().BeNull();

        await service.RunAssessmentAsync(NoFilter());

        (await service.GetAnalysisAsync(NoFilter())).LastRecordedRunAt.Should().Be(FixedToday);
    }

    [Fact]
    public async Task RunAssessmentAsync_SavesAnomalyFieldsForInvestigatorFollowUp()
    {
        AddReferenceDataset();
        var service = CreateService();

        await service.RunAssessmentAsync(NoFilter());

        var spike = await _context.Anomalies.AsNoTracking()
            .SingleAsync(a => a.AnomalyType == AnomalyTypes.VolumeSpike);

        spike.RelatedEntityId.Should().Be(_centralId);
        spike.RelatedEntityType.Should().Be("District");
        spike.Severity.Should().Be(AnomalySeverity.Critical);
        spike.DetectedPatterns.Should().Be("Volume spike");
        spike.ConfidenceScore.Should().Be(0.714m); // 5 of the combined 7 observations
        spike.Status.Should().Be(1);
        spike.DetectedById.Should().Be(_actorId);
    }
}
