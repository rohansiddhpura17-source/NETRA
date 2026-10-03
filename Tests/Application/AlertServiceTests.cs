using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NETRA.Application.Abstractions;
using NETRA.Application.Alerts;
using NETRA.Application.Analytics;
using NETRA.Application.Risk;
using NETRA.Domain.Alerts;
using NETRA.Domain.Cases;
using NETRA.Domain.Enums;
using NETRA.Domain.Geography;
using NETRA.Domain.People;
using NETRA.Domain.Audit;
using NETRA.Domain.Security;
using NETRA.Domain.Settings;
using NETRA.Infrastructure.Data;
using NETRA.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace NETRA.Tests.Application;

/// <summary>
/// Service-level tests for <see cref="AlertService"/>: deterministic generation
/// from the existing analytical subsystems, AlertKey deduplication (a second run
/// must create nothing), server-side filtering, lifecycle transitions and the
/// preference-respecting notification fan-out. Hosted on EF Core's in-memory
/// provider; these are not live SQL Server tests.
/// </summary>
public sealed class AlertServiceTests : IDisposable
{
    // Fixed clock: default 30-day generation window is 2026-05-17..2026-06-15.
    private static readonly DateTimeOffset FixedInstant = new(2026, 6, 15, 9, 30, 0, TimeSpan.Zero);

    private static readonly DateTime FixedNow = FixedInstant.UtcDateTime;

    private readonly NetraDbContext _context;
    private readonly Guid _actorId = Guid.NewGuid();
    private readonly Guid _analystId = Guid.NewGuid();
    private readonly Guid _investigatorId = Guid.NewGuid();
    private readonly Guid _adminId = Guid.NewGuid();
    private readonly Guid _centralDistrictId = Guid.NewGuid();
    private readonly Guid _suspectPersonId = Guid.NewGuid();
    private Guid _firstCaseId;

    public AlertServiceTests()
    {
        _context = new NetraDbContext(
            new DbContextOptionsBuilder<NetraDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        SeedAsync().GetAwaiter().GetResult();
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

    /// <summary>Returns a scripted analysis, standing in for the risk subsystem.</summary>
    private sealed class StubRiskAnalysisService : IRiskAnalysisService
    {
        private readonly RiskAnalysisDto _analysis;
        public StubRiskAnalysisService(RiskAnalysisDto analysis) => _analysis = analysis;

        public Task<RiskAnalysisDto> GetAnalysisAsync(
            AnalyticsFilterCriteria criteria, CancellationToken cancellationToken = default) =>
            Task.FromResult(_analysis);

        public Task<RiskAssessmentRunResultDto> RunAssessmentAsync(
            AnalyticsFilterCriteria criteria, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private RiskIndicatorDto HighRiskIndicator(int level = 5) => new()
    {
        DistrictId = _centralDistrictId,
        DistrictName = "Central District",
        DistrictCode = "CD",
        CurrentCount = 12,
        BaselineCount = 3,
        HighSeverityCount = 4,
        CriticalSeverityCount = 2,
        ChangePercent = 300m,
        Score = 82m,
        Level = level,
        LevelLabel = level >= 5 ? "Critical" : "High",
        Explanation = $"Central District recorded 12 cases against a baseline of 3 (level {level})."
    };

    private AnomalyCandidateDto VolumeSpikeAnomaly() => new()
    {
        Title = "Volume spike in Central District",
        Description = "Case volume in Central District rose 300% against the comparison window.",
        Severity = "High",
        AnomalyType = AnomalyTypes.VolumeSpike,
        DistrictId = _centralDistrictId,
        DistrictName = "Central District",
        DeviationPercent = 300m,
        Confidence = 0.9m
    };

    private RiskAnalysisDto Analysis(
        IReadOnlyList<RiskIndicatorDto>? indicators = null,
        IReadOnlyList<AnomalyCandidateDto>? anomalies = null) => new()
    {
        DateFrom = FixedNow.AddDays(-29),
        DateTo = FixedNow,
        ComparisonFrom = FixedNow.AddDays(-59),
        ComparisonTo = FixedNow.AddDays(-30),
        Indicators = indicators ?? new List<RiskIndicatorDto> { HighRiskIndicator() },
        Anomalies = anomalies ?? new List<AnomalyCandidateDto> { VolumeSpikeAnomaly() },
        TotalCasesInWindow = 12
    };

    private AlertService CreateService(RiskAnalysisDto? analysis = null) =>
        new(
            _context,
            new StubRiskAnalysisService(analysis ?? Analysis()),
            new AuditService(
                _context,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
                new StubActorAccessor(_actorId)),
            new FixedTimeProvider(FixedInstant));

    private async Task SeedAsync()
    {
        _context.Users.AddRange(
            User(_actorId, "alert.seed@netra.local", "Alert", "Seed"),
            User(_analystId, "alert.analyst@netra.local", "Ada", "Analyst"),
            User(_investigatorId, "alert.investigator@netra.local", "Ivan", "Investigator"),
            User(_adminId, "alert.admin@netra.local", "Asta", "Administrator"));

        var analystRole = new IdentityRole<Guid>("Police / Intelligence Analyst")
        {
            Id = Guid.NewGuid(),
            NormalizedName = "POLICE / INTELLIGENCE ANALYST"
        };
        var investigatorRole = new IdentityRole<Guid>("Investigator")
        {
            Id = Guid.NewGuid(),
            NormalizedName = "INVESTIGATOR"
        };
        var adminRole = new IdentityRole<Guid>("System Administrator")
        {
            Id = Guid.NewGuid(),
            NormalizedName = "SYSTEM ADMINISTRATOR"
        };
        _context.Roles.AddRange(analystRole, investigatorRole, adminRole);
        _context.UserRoles.AddRange(
            new IdentityUserRole<Guid> { UserId = _analystId, RoleId = analystRole.Id },
            new IdentityUserRole<Guid> { UserId = _investigatorId, RoleId = investigatorRole.Id },
            new IdentityUserRole<Guid> { UserId = _adminId, RoleId = adminRole.Id });

        _context.Districts.AddRange(
            new District { Id = _centralDistrictId, Name = "Central District", Code = "CD" },
            new District { Name = "North District", Code = "ND" });

        var theft = new CrimeType { Name = "Theft", Code = "THF", DisplayOrder = 1, IsActive = true };
        _context.CrimeTypes.Add(theft);

        var suspect = new Person
        {
            Id = _suspectPersonId,
            FirstName = "Sam",
            LastName = "Suspect",
            CreatedById = _actorId
        };
        _context.Persons.Add(suspect);

        // Two recent cases naming the same suspect, one too old to qualify.
        var incidentDates = new[] { FixedNow.AddDays(-5), FixedNow.AddDays(-10), FixedNow.AddDays(-200) };
        var caseIds = new List<Guid>();
        foreach (var (incidentDate, index) in incidentDates.Select((d, i) => (d, i)))
        {
            var crimeCase = new CrimeCase
            {
                CaseNumber = $"ALR-WEB-00{index + 1}",
                Title = $"Alert seed case {index + 1}",
                CrimeTypeId = theft.Id,
                Status = CaseStatus.Open,
                Severity = CaseSeverity.Medium,
                IncidentDate = incidentDate,
                CreatedById = _actorId
            };
            _context.CrimeCases.Add(crimeCase);
            caseIds.Add(crimeCase.Id);
        }

        _firstCaseId = caseIds[0];
        _context.CasePersons.AddRange(
            new CasePerson { CrimeCaseId = caseIds[0], PersonId = suspect.Id, Role = PersonRole.Suspect },
            new CasePerson { CrimeCaseId = caseIds[1], PersonId = suspect.Id, Role = PersonRole.Suspect },
            new CasePerson { CrimeCaseId = caseIds[2], PersonId = suspect.Id, Role = PersonRole.Suspect });

        // Five recorded failed sign-ins cross the security-alert threshold.
        for (var i = 0; i < 5; i++)
        {
            _context.AuditLogs.Add(new AuditLog
            {
                EntityType = AuditEntityTypeNames.Account,
                EntityId = _actorId,
                Action = AuditActionNames.SignInFailed,
                UserId = _actorId,
                Timestamp = FixedNow.AddDays(-2)
            });
        }

        await _context.SaveChangesAsync();
    }

    private ApplicationUser User(Guid id, string email, string firstName, string lastName) => new()
    {
        Id = id,
        UserName = email,
        Email = email,
        FirstName = firstName,
        LastName = lastName,
        EmailConfirmed = true,
        IsActive = true
    };

    private static Alert SeedAlert(
        AlertCategory category, AlertSeverity severity, AlertStatus status, int order = 0) => new()
    {
        AlertType = (int)category,
        Severity = severity,
        Status = status,
        Title = $"{category} {severity} {status} #{order}",
        Description = "Seeded alert used for reading and lifecycle coverage.",
        CreatedAt = FixedNow.AddDays(-order)
    };

    // ------------------------------------------------------------------
    // Generation
    // ------------------------------------------------------------------

    [Fact]
    public async Task Generate_FirstRun_CreatesOneAlertPerAnalyticalSource()
    {
        var service = CreateService();

        var result = await service.GenerateAsync(30, _actorId);

        result.CreatedCount.Should().Be(4);
        result.SkippedDuplicateCount.Should().Be(0);
        result.WindowStart.Should().Be(FixedNow.Date.AddDays(-29));
        result.WindowEnd.Should().Be(FixedNow.Date);

        var alerts = await _context.Alerts.AsNoTracking().ToListAsync();
        alerts.Select(a => (AlertCategory)a.AlertType).Should().BeEquivalentTo(new[]
        {
            AlertCategory.HighRiskArea,
            AlertCategory.CrimeSpike,
            AlertCategory.RelatedCases,
            AlertCategory.SecuritySystem
        });

        // The analytical basis is the existing risk explanation, not a new formula.
        var riskAlert = alerts.Single(a => a.AlertType == (int)AlertCategory.HighRiskArea);
        riskAlert.Description.Should().Be(HighRiskIndicator().Explanation);
        riskAlert.Severity.Should().Be(AlertSeverity.Critical);
        riskAlert.RelatedEntityType.Should().Be("District");
        riskAlert.Status.Should().Be(AlertStatus.New);
        alerts.Single(a => a.AlertType == (int)AlertCategory.RelatedCases)
            .Description.Should().Contain("recorded case data, not a finding of involvement");
    }

    [Fact]
    public async Task Generate_SecondRun_CreatesNoDuplicates()
    {
        var service = CreateService();

        var first = await service.GenerateAsync(30, _actorId);
        var countAfterFirst = await _context.Alerts.CountAsync();

        var second = await service.GenerateAsync(30, _actorId);
        var countAfterSecond = await _context.Alerts.CountAsync();

        first.CreatedCount.Should().Be(4);
        second.CreatedCount.Should().Be(0);
        second.SkippedDuplicateCount.Should().Be(4);
        countAfterSecond.Should().Be(countAfterFirst);

        // Notifications are idempotent per recipient and alert as well.
        (await _context.Notifications.CountAsync())
            .Should().Be(await _context.Notifications.CountAsync());
        var perRecipient = await _context.Notifications
            .GroupBy(n => n.RecipientId)
            .Select(g => g.Count())
            .ToListAsync();
        perRecipient.Should().OnlyContain(count => count <= 4);
    }

    [Fact]
    public async Task Generate_WindowDaysAreClamped()
    {
        var service = CreateService();

        var low = await service.GenerateAsync(1, _actorId);
        var high = await service.GenerateAsync(5000, _actorId);

        low.WindowDays.Should().Be(AlertLimits.MinGenerationWindowDays);
        high.WindowDays.Should().Be(AlertLimits.MaxGenerationWindowDays);

        // The 1-day window saw no security failures (they happened two days ago),
        // so the first run created 3 alerts and the full-year window adds its own
        // 4 under different keys — different window, different analysis, not a
        // duplicate of anything.
        (await _context.Alerts.CountAsync()).Should().Be(7);

        // Re-running the same window still adds nothing.
        var third = await service.GenerateAsync(5000, _actorId);
        third.CreatedCount.Should().Be(0);
        third.SkippedDuplicateCount.Should().Be(4);
    }

    [Fact]
    public async Task Generate_InsufficientDataAndLowLevels_ProduceNothing()
    {
        var analysis = Analysis(
            new List<RiskIndicatorDto>
            {
                new()
                {
                    DistrictId = _centralDistrictId,
                    DistrictName = "Central District",
                    DistrictCode = "CD",
                    Level = 5,
                    InsufficientData = true,
                    Explanation = "Sample too small."
                },
                HighRiskIndicator(level: 3)
            },
            new List<AnomalyCandidateDto>());
        var service = CreateService(analysis);

        var result = await service.GenerateAsync(30, _actorId);

        // Only the recorded association and security candidates remain.
        result.CreatedCount.Should().Be(2);
        (await _context.Alerts.AnyAsync(a => a.AlertType == (int)AlertCategory.HighRiskArea))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Generate_CreatesNotificationsPerRole_RespectingDisabledPreferences()
    {
        _context.NotificationPreferences.Add(new NotificationPreference
        {
            UserId = _investigatorId,
            NotificationType = (int)NotificationCategory.Investigation,
            IsEnabled = false
        });
        await _context.SaveChangesAsync();

        var service = CreateService();

        await service.GenerateAsync(30, _actorId);

        var recipients = await _context.Notifications.AsNoTracking()
            .GroupBy(n => n.RecipientId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToListAsync();

        // Crime-intelligence alerts: analyst + admin. Related cases: investigator
        // opted out, so only admin. Security/system: admin only.
        recipients.Single(r => r.UserId == _analystId).Count.Should().Be(2);
        recipients.Single(r => r.UserId == _adminId).Count.Should().Be(4);
        recipients.Any(r => r.UserId == _investigatorId).Should().BeFalse();

        var channels = await _context.Notifications.Select(n => n.DeliveryChannel).Distinct().ToListAsync();
        channels.Should().BeEquivalentTo(new[] { 1 });
    }

    // ------------------------------------------------------------------
    // Reading, filtering, summary
    // ------------------------------------------------------------------

    [Fact]
    public async Task ListAsync_FiltersAreServerSide_AndValuesPersist()
    {
        _context.Alerts.AddRange(
            SeedAlert(AlertCategory.CrimeSpike, AlertSeverity.Critical, AlertStatus.New, 0),
            SeedAlert(AlertCategory.HighRiskArea, AlertSeverity.High, AlertStatus.Acknowledged, 1),
            SeedAlert(AlertCategory.RelatedCases, AlertSeverity.Medium, AlertStatus.Resolved, 2));
        await _context.SaveChangesAsync();

        var service = CreateService();

        var all = await service.ListAsync(new AlertFilterCriteria());
        var critical = await service.ListAsync(new AlertFilterCriteria { Severity = AlertSeverity.Critical });
        var acknowledged = await service.ListAsync(new AlertFilterCriteria { Status = AlertStatus.Acknowledged });
        var areaCategory = await service.ListAsync(new AlertFilterCriteria { Category = AlertCategory.HighRiskArea });
        var resolved = await service.ListAsync(new AlertFilterCriteria { Status = AlertStatus.Resolved });

        all.TotalCount.Should().Be(3);
        critical.Items.Should().ContainSingle(a => a.Severity == AlertSeverity.Critical);
        acknowledged.Items.Should().ContainSingle(a => a.Category == AlertCategory.HighRiskArea);
        areaCategory.Items.Should().ContainSingle(a => a.Status == AlertStatus.Acknowledged);
        resolved.Items.Should().ContainSingle(a => a.IsGenerated == false);
    }

    [Fact]
    public async Task ListAsync_DateRangeIsInclusiveOfFinalDay_AndPaginates()
    {
        for (var i = 0; i < 5; i++)
        {
            _context.Alerts.Add(SeedAlert(AlertCategory.CrimeSpike, AlertSeverity.Low, AlertStatus.New, i));
        }

        _context.Alerts.Add(new Alert
        {
            AlertType = (int)AlertCategory.CrimeSpike,
            Severity = AlertSeverity.Low,
            Status = AlertStatus.New,
            Title = "Outside range",
            CreatedAt = FixedNow.Date.AddDays(1).AddHours(23)
        });
        await _context.SaveChangesAsync();

        var service = CreateService();

        var inRange = await service.ListAsync(new AlertFilterCriteria
        {
            DateFrom = FixedNow.AddDays(-6),
            DateTo = FixedNow.Date,
            PageSize = 2
        });

        inRange.TotalCount.Should().Be(5);
        inRange.Items.Should().HaveCount(2);
        inRange.TotalPages.Should().Be(3);
        inRange.Page.Should().Be(1);
    }

    [Fact]
    public async Task ListAsync_RelatedEntityLabelsComeFromLinkedRecords()
    {
        var districtAlert = SeedAlert(AlertCategory.HighRiskArea, AlertSeverity.High, AlertStatus.New);
        districtAlert.RelatedEntityId = _centralDistrictId;
        districtAlert.RelatedEntityType = "District";

        var personAlert = SeedAlert(AlertCategory.RelatedCases, AlertSeverity.Medium, AlertStatus.New, 1);
        personAlert.RelatedEntityId = _suspectPersonId;
        personAlert.RelatedEntityType = "Person";

        var caseAlert = SeedAlert(AlertCategory.CrimeSpike, AlertSeverity.Low, AlertStatus.New, 2);
        caseAlert.CrimeCaseId = _firstCaseId;

        _context.Alerts.AddRange(districtAlert, personAlert, caseAlert);
        await _context.SaveChangesAsync();

        var service = CreateService();

        var items = (await service.ListAsync(new AlertFilterCriteria())).Items;

        items.Single(a => a.Id == districtAlert.Id).RelatedEntityLabel.Should().Be("Central District");
        items.Single(a => a.Id == personAlert.Id).RelatedEntityLabel.Should().Be("Sam Suspect");
        items.Single(a => a.Id == caseAlert.Id).CaseNumber.Should().Be("ALR-WEB-001");
    }

    [Fact]
    public async Task GetSummaryAsync_CountsComeFromTheDatabase()
    {
        _context.Alerts.AddRange(
            SeedAlert(AlertCategory.CrimeSpike, AlertSeverity.Critical, AlertStatus.New, 0),
            SeedAlert(AlertCategory.CrimeSpike, AlertSeverity.Critical, AlertStatus.Resolved, 1),
            SeedAlert(AlertCategory.HighRiskArea, AlertSeverity.High, AlertStatus.Acknowledged, 2),
            SeedAlert(AlertCategory.RelatedCases, AlertSeverity.Medium, AlertStatus.Dismissed, 3));
        await _context.SaveChangesAsync();

        var service = CreateService();

        var summary = await service.GetSummaryAsync();

        summary.CriticalCount.Should().Be(1);
        summary.HighCount.Should().Be(1);
        summary.OpenCount.Should().Be(2);
        summary.TotalCount.Should().Be(4);
    }

    [Fact]
    public async Task GetCaseOptionsAsync_ListsRecentCaseNumbers()
    {
        var service = CreateService();

        var options = await service.GetCaseOptionsAsync();

        options.Should().Contain(o => o.CaseNumber == "ALR-WEB-001");
    }

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    [Fact]
    public async Task Acknowledge_SetsStatusTimestampAndAudit()
    {
        var alert = SeedAlert(AlertCategory.CrimeSpike, AlertSeverity.High, AlertStatus.New);
        _context.Alerts.Add(alert);
        await _context.SaveChangesAsync();

        var service = CreateService();

        (await service.AcknowledgeAsync(alert.Id, _actorId)).Succeeded.Should().BeTrue();

        var stored = await _context.Alerts.AsNoTracking().FirstAsync(a => a.Id == alert.Id);
        stored.Status.Should().Be(AlertStatus.Acknowledged);
        stored.AcknowledgedAt.Should().Be(FixedNow);
        (await _context.AuditLogs.AnyAsync(l =>
            l.Action == AuditActionNames.AlertAcknowledged &&
            l.EntityId == alert.Id &&
            l.UserId == _actorId)).Should().BeTrue();
    }

    [Fact]
    public async Task Resolve_StoresNotesAndResolver()
    {
        var alert = SeedAlert(AlertCategory.HighRiskArea, AlertSeverity.Critical, AlertStatus.Acknowledged);
        _context.Alerts.Add(alert);
        await _context.SaveChangesAsync();

        var service = CreateService();

        (await service.ResolveAsync(alert.Id, "Patrols dispatched; volume normalised.", _actorId))
            .Succeeded.Should().BeTrue();

        var stored = await _context.Alerts.AsNoTracking().FirstAsync(a => a.Id == alert.Id);
        stored.Status.Should().Be(AlertStatus.Resolved);
        stored.ResolvedAt.Should().Be(FixedNow);
        stored.ResolvedById.Should().Be(_actorId);
        stored.ResolutionNotes.Should().Be("Patrols dispatched; volume normalised.");
    }

    [Fact]
    public async Task Transitions_OnClosedAlert_Conflict()
    {
        var alert = SeedAlert(AlertCategory.CrimeSpike, AlertSeverity.High, AlertStatus.Resolved);
        _context.Alerts.Add(alert);
        await _context.SaveChangesAsync();

        var service = CreateService();

        var dismiss = await service.DismissAsync(alert.Id, "too late", _actorId);

        dismiss.Status.Should().Be(OperationStatus.Conflict);
        dismiss.Errors.Values.SelectMany(e => e).Should().ContainSingle()
            .Which.Should().Contain("already resolved");
        (await _context.AuditLogs.AnyAsync(l => l.Action == AuditActionNames.AlertDismissed))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Transitions_UnknownAlert_ReturnsNotFound()
    {
        var service = CreateService();

        var result = await service.AcknowledgeAsync(Guid.NewGuid(), _actorId);

        result.Status.Should().Be(OperationStatus.NotFound);
    }

    [Fact]
    public async Task GetDetailAsync_AuditsTheViewingActor()
    {
        var alert = SeedAlert(AlertCategory.RelatedCases, AlertSeverity.Medium, AlertStatus.New);
        alert.RelatedEntityId = _suspectPersonId;
        alert.RelatedEntityType = "Person";
        alert.CrimeCaseId = _firstCaseId;
        _context.Alerts.Add(alert);
        await _context.SaveChangesAsync();

        var service = CreateService();

        var detail = await service.GetDetailAsync(alert.Id, _analystId);

        detail.Should().NotBeNull();
        detail!.Category.Should().Be(AlertCategory.RelatedCases);
        detail.RelatedEntityLabel.Should().Be("Sam Suspect");
        detail.CaseNumber.Should().Be("ALR-WEB-001");
        detail.IsGenerated.Should().BeFalse();
        (await _context.AuditLogs.AnyAsync(l =>
            l.Action == AuditActionNames.AlertViewed &&
            l.EntityId == alert.Id &&
            l.UserId == _analystId)).Should().BeTrue();

        (await service.GetDetailAsync(Guid.NewGuid(), _analystId)).Should().BeNull();
    }

    [Fact]
    public async Task Generate_WritesAuditRowPerCreatedAlert()
    {
        var service = CreateService();

        await service.GenerateAsync(30, _actorId);

        var generated = await _context.AuditLogs.AsNoTracking()
            .Where(l => l.Action == AuditActionNames.AlertGenerated)
            .ToListAsync();

        generated.Should().HaveCount(4);
        generated.Should().AllSatisfy(entry =>
        {
            entry.UserId.Should().Be(_actorId);
            entry.PropertyName.Should().Be(nameof(Alert.AlertKey));
        });
        (await _context.AuditLogs.AnyAsync(l => l.Action == AuditActionNames.NotificationCreated))
            .Should().BeTrue();
    }

    [Fact]
    public async Task AlertKeys_AreStableAndDeterministic()
    {
        var start = new DateTime(2026, 5, 17, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        var source = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var first = AlertKeys.Build(AlertCategory.HighRiskArea, "District", source, start, end);
        var second = AlertKeys.Build(AlertCategory.HighRiskArea, "District", source, start, end);
        var otherWindow = AlertKeys.Build(AlertCategory.HighRiskArea, "District", source, start, end.AddDays(1));

        first.Should().Be(second);
        first.Should().NotBe(otherWindow);
        first.Should().StartWith($"3:District:{source:N}:");
        AlertCategories.Label(AlertCategory.HighRiskArea).Should().Be("High-Risk Area");
        AlertCategories.TryParse("crimeSpike", out var parsed).Should().BeTrue();
        parsed.Should().Be(AlertCategory.CrimeSpike);
        AlertCategories.TryParse("nonsense", out _).Should().BeFalse();
    }
}
