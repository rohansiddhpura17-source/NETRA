using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NETRA.Application.Abstractions;
using NETRA.Application.Analytics;
using NETRA.Application.Reports;
using NETRA.Domain.Alerts;
using NETRA.Domain.Cases;
using NETRA.Domain.Enums;
using NETRA.Domain.Geography;
using NETRA.Domain.Investigation;
using NETRA.Domain.People;
using NETRA.Domain.Reports;
using NETRA.Domain.Security;
using NETRA.Infrastructure.Data;
using NETRA.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace NETRA.Tests.Application;

/// <summary>
/// Service-level tests for <see cref="ReportService"/>. The real case, analytics, risk,
/// dashboard and alert services are composed over one in-memory database, so every
/// assertion checks that reports reuse existing subsystems instead of inventing a second
/// formula or fabricating rows. Covers generation per type, data minimisation, storage of
/// parameters rather than data, filtering, search, pagination, analyst authoring,
/// immutability, lifecycle, CSV export and audit. Not live SQL Server tests.
/// </summary>
public sealed class ReportServiceTests : IDisposable
{
    // Fixed clock: the default generation window is 17 May – 15 Jun 2026.
    private static readonly DateTimeOffset FixedInstant = new(2026, 6, 15, 9, 30, 0, TimeSpan.Zero);

    private static readonly DateTime FixedNow = FixedInstant.UtcDateTime;

    private readonly NetraDbContext _context;
    private readonly Guid _actorId = Guid.NewGuid();
    private readonly Guid _secondUserId = Guid.NewGuid();
    private readonly Guid _centralId = Guid.NewGuid();
    private readonly Guid _theftId = Guid.NewGuid();
    private readonly Guid _robberyId = Guid.NewGuid();
    private readonly Guid _personId = Guid.NewGuid();
    private readonly Guid _witnessId = Guid.NewGuid();
    private Guid _focusCaseId;
    private Guid _linkedCaseId;

    public ReportServiceTests()
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

    private sealed class NullFileStorageService : IFileStorageService
    {
        public IReadOnlyCollection<string> AllowedExtensions => Array.Empty<string>();

        public long MaxFileSizeBytes => 0;

        public FileValidationResult Validate(string fileName, string contentType, long length) =>
            FileValidationResult.Invalid("Storage is not used in these tests.");

        public Task<StoredFileResult> SaveAsync(Stream content, string originalFileName, string contentType,
            string caseFolder, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(null);
    }

    private AuditService Audit() => new(
        _context,
        new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
        new StubActorAccessor(_actorId));

    private ReportService CreateService()
    {
        var time = new FixedTimeProvider(FixedInstant);
        var analytics = new AnalyticsService(_context, time);
        var risk = new RiskAnalysisService(_context, new AnomalyDetector(), time, new StubActorAccessor(_actorId), Audit());

        return new ReportService(
            _context,
            Audit(),
            time,
            new CaseService(_context, Audit(), new NullFileStorageService()),
            analytics,
            risk,
            new DashboardService(_context, analytics, time),
            new AlertService(_context, risk, Audit(), time));
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

    private async Task SeedAsync()
    {
        _context.Users.AddRange(
            User(_actorId, "report.seed@netra.local", "Ada", "Analyst"),
            User(_secondUserId, "report.second@netra.local", "Omar", "Officer"));

        _context.Roles.Add(new IdentityRole<Guid>("Police / Intelligence Analyst")
        {
            Id = Guid.NewGuid(),
            Name = "Police / Intelligence Analyst",
            NormalizedName = "POLICE / INTELLIGENCE ANALYST"
        });

        _context.Districts.AddRange(
            new District { Id = _centralId, Name = "Central District", Code = "CD", IsActive = true },
            new District { Name = "North District", Code = "ND", IsActive = true });

        _context.CrimeTypes.AddRange(
            new CrimeType { Id = _theftId, Name = "Theft", Code = "THF", DisplayOrder = 1, IsActive = true },
            new CrimeType { Id = _robberyId, Name = "Robbery", Code = "ROB", DisplayOrder = 2, IsActive = true });

        await _context.SaveChangesAsync();

        _context.PoliceStations.Add(new PoliceStation
        {
            Name = "Central HQ", Code = "CHQ", DistrictId = _centralId, IsActive = true
        });

        // A person whose sensitive attributes must never reach a report.
        _context.Persons.Add(new Person
        {
            Id = _personId,
            FirstName = "Dana",
            LastName = "Demo",
            NationalIdNumber = "NID-SECRET-9999",
            PhoneNumber = "555-0100",
            Email = "dana@private.example",
            Address = "12 Private Lane",
            CreatedById = _actorId,
            IsActive = true
        });
        _context.Persons.Add(new Person
        {
            Id = _witnessId,
            FirstName = "Wita",
            LastName = "Witness",
            NationalIdNumber = "NID-SECRET-8888",
            PhoneNumber = "555-0101",
            Email = "wita@private.example",
            CreatedById = _actorId,
            IsActive = true
        });

        await _context.SaveChangesAsync();

        _focusCaseId = await AddCaseAsync("RPT-2026-0101", FixedNow.AddDays(-5), _theftId, _centralId,
            CaseSeverity.High, CaseStatus.Open);
        _linkedCaseId = await AddCaseAsync("RPT-2026-0102", FixedNow.AddDays(-9), _theftId, _centralId);
        await AddCaseAsync("RPT-2026-0103", FixedNow.AddDays(-12), _robberyId, _centralId, CaseSeverity.Critical);
        await AddCaseAsync("RPT-2026-0104", FixedNow.AddDays(-2), _robberyId, _centralId, CaseSeverity.Medium,
            CaseStatus.Closed);
        await AddCaseAsync("RPT-2026-0105", FixedNow.AddDays(-3), _theftId, await NorthIdAsync());
        // Inside the risk comparison window (17 Apr – 16 May), so indicators have a baseline.
        await AddCaseAsync("RPT-2026-0106", FixedNow.AddDays(-35), _theftId, _centralId);

        _context.CasePersons.AddRange(
            new CasePerson
            {
                CrimeCaseId = _focusCaseId,
                PersonId = _personId,
                Role = PersonRole.Suspect,
                AssociatedAt = FixedNow.AddDays(-4),
                AssociatedById = _actorId
            },
            new CasePerson
            {
                CrimeCaseId = _focusCaseId,
                PersonId = _witnessId,
                Role = PersonRole.Witness,
                AssociatedAt = FixedNow.AddDays(-4),
                AssociatedById = _actorId
            },
            // The same suspect appears in a second case: a recorded association, not a finding.
            new CasePerson
            {
                CrimeCaseId = _linkedCaseId,
                PersonId = _personId,
                Role = PersonRole.Suspect,
                AssociatedAt = FixedNow.AddDays(-8),
                AssociatedById = _actorId
            });

        var investigation = new Investigation
        {
            CrimeCaseId = _focusCaseId,
            Title = "Investigation — RPT-2026-0101",
            Summary = "Scene review and statement collection.",
            StartedAt = FixedNow.AddDays(-5),
            LeadInvestigatorId = _secondUserId,
            Status = 1,
            CreatedAt = FixedNow.AddDays(-5)
        };
        _context.Investigations.Add(investigation);
        await _context.SaveChangesAsync();

        var cctv = new Evidence
        {
            InvestigationId = investigation.Id,
            Title = "CCTV segment",
            EvidenceType = 2,
            EvidenceStatus = 1,
            CollectedAt = FixedNow.AddDays(-4),
            CreatedAt = FixedNow.AddDays(-4)
        };
        var statement = new Evidence
        {
            InvestigationId = investigation.Id,
            Title = "Witness statement",
            EvidenceType = 1,
            EvidenceStatus = 2,
            CollectedAt = FixedNow.AddDays(-3),
            CreatedAt = FixedNow.AddDays(-3)
        };
        _context.Evidence.AddRange(cctv, statement);

        // Raw evidence files are deliberately excluded from report content.
        _context.EvidenceFiles.Add(new EvidenceFile
        {
            EvidenceId = cctv.Id,
            FileName = "raw-cctv-footage.mp4",
            FileExtension = "mp4",
            ContentType = "video/mp4",
            FileSizeBytes = 4096,
            StoragePath = "cases/raw-cctv-footage.mp4",
            ChecksumSha256 = "deadbeefchecksum",
            UploadedById = _actorId,
            UploadedAt = FixedNow.AddDays(-4)
        });

        _context.InvestigationNotes.AddRange(
            new InvestigationNote
            {
                InvestigationId = investigation.Id,
                Content = "Public observation: CCTV reviewed with the station.",
                NoteType = 1,
                CreatedAt = FixedNow.AddDays(-3),
                CreatedById = _secondUserId,
                IsPrivate = false
            },
            new InvestigationNote
            {
                InvestigationId = investigation.Id,
                Content = "Restricted note: informant payment details.",
                NoteType = 2,
                CreatedAt = FixedNow.AddDays(-2),
                CreatedById = _secondUserId,
                IsPrivate = true
            });

        _context.TimelineEvents.Add(new TimelineEvent
        {
            InvestigationId = investigation.Id,
            Title = "Case opened",
            EventDateTime = FixedNow.AddDays(-5),
            EventType = 1,
            CreatedAt = FixedNow.AddDays(-5),
            CreatedById = _secondUserId
        });

        _context.Alerts.Add(new Alert
        {
            AlertType = (int)AlertCategory.CrimeSpike,
            Severity = AlertSeverity.Critical,
            Status = AlertStatus.New,
            Title = "RPT seed volume spike",
            Description = "Case volume rose against the comparison window.",
            CreatedAt = FixedNow.AddDays(-1)
        });

        await _context.SaveChangesAsync();
    }

    private async Task<Guid> NorthIdAsync() =>
        (await _context.Districts.FirstAsync(d => d.Code == "ND")).Id;

    private async Task<Guid> AddCaseAsync(
        string caseNumber, DateTime incidentDate, Guid crimeTypeId, Guid districtId,
        CaseSeverity severity = CaseSeverity.Medium, CaseStatus status = CaseStatus.Open)
    {
        var crimeCase = new CrimeCase
        {
            CaseNumber = caseNumber,
            Title = $"Case {caseNumber}",
            CrimeTypeId = crimeTypeId,
            Status = status,
            Severity = severity,
            IncidentDate = incidentDate,
            AssignedInvestigatorId = caseNumber == "RPT-2026-0101" ? _secondUserId : null,
            CreatedById = _actorId,
            CreatedAt = incidentDate
        };
        _context.CrimeCases.Add(crimeCase);

        var location = new Location
        {
            Name = $"{caseNumber} scene",
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
        return crimeCase.Id;
    }

    private Report SeedReport(
        string title, ReportType type, ReportSource source = ReportSource.SystemGenerated,
        ReportStatus status = ReportStatus.Ready, DateTime? createdAt = null, Guid? caseId = null,
        string? summary = null, string? content = null, Guid? authorId = null)
    {
        var report = new Report
        {
            Title = title,
            ReportType = type,
            Source = source,
            Status = status,
            Description = summary ?? $"{title} is a seeded report used for list coverage.",
            Content = content,
            CrimeCaseId = caseId,
            PeriodFrom = FixedNow.AddDays(-29).Date,
            PeriodTo = FixedNow.Date,
            RequestedById = authorId ?? _actorId,
            RequestedAt = createdAt ?? FixedNow,
            CreatedAt = createdAt ?? FixedNow
        };
        _context.Reports.Add(report);
        return report;
    }

    /// <summary>Every string a reader can see in a document, flattened for minimisation checks.</summary>
    private static string Flatten(ReportDocumentDto document)
    {
        var parts = new List<string>
        {
            document.Title, document.Summary ?? string.Empty,
            document.AnalystContent ?? string.Empty,
            document.CaseNumber ?? string.Empty,
            document.AuthorName ?? string.Empty
        };

        parts.AddRange(document.AppliedFilters);
        parts.AddRange(document.Findings);
        parts.AddRange(document.Metrics.Select(m => $"{m.Label} {m.Value} {m.Meta}"));
        parts.AddRange(document.LinkedCases.Select(c => $"{c.Title} {c.CaseNumber} {c.Status}"));

        foreach (var section in document.Sections)
        {
            parts.Add(section.Heading);
            parts.AddRange(section.Rows);
            parts.AddRange(section.Fields.Select(f => $"{f.Label} {f.Value}"));
            parts.AddRange(section.Columns);
            foreach (var row in section.Table)
            {
                parts.AddRange(row);
            }
        }

        return string.Join('\n', parts);
    }

    private static GenerateReportRequest Generate(
        ReportType type, Guid? caseId = null, DateTime? from = null, DateTime? to = null,
        string? observations = null, Guid? districtId = null, Guid? crimeTypeId = null,
        string? severity = null) => new()
        {
            Type = type,
            CaseId = caseId,
            DistrictId = districtId,
            CrimeTypeId = crimeTypeId,
            Severity = severity,
            DateFrom = from,
            DateTo = to,
            AnalystObservations = observations
        };

    // ------------------------------------------------------------------
    // Case Summary and Investigation Summary
    // ------------------------------------------------------------------

    [Fact]
    public async Task Generate_CaseSummary_BuildsFromRecordedCaseFactsOnly()
    {
        var service = CreateService();

        var result = await service.GenerateAsync(Generate(ReportType.CaseSummary, _focusCaseId), _actorId, true);

        result.Succeeded.Should().BeTrue();
        var stored = await _context.Reports.FirstAsync(r => r.Id == result.Value);
        stored.Source.Should().Be(ReportSource.SystemGenerated);
        stored.Status.Should().Be(ReportStatus.Ready);
        stored.CrimeCaseId.Should().Be(_focusCaseId);
        stored.Title.Should().Be("Case Summary — RPT-2026-0101");
        stored.CreatedAt.Should().Be(FixedNow);

        var document = await service.GetDocumentAsync(result.Value, _actorId, true);
        document!.HasData.Should().BeTrue();

        var classification = document.Sections.Single(s => s.Heading == "Case classification");
        classification.Fields.Single(f => f.Label == "Case number").Value.Should().Be("RPT-2026-0101");
        classification.Fields.Single(f => f.Label == "Crime type").Value.Should().Be("Theft");
        classification.Fields.Single(f => f.Label == "Severity").Value.Should().Be("High");
        classification.Fields.Single(f => f.Label == "Status").Value.Should().Be("Open");
        classification.Fields.Single(f => f.Label == "District").Value.Should().Be("Central District");
        classification.Fields.Single(f => f.Label == "Assigned investigator").Value.Should().Be("Omar Officer");

        var persons = document.Sections.Single(s => s.Heading == "Persons of interest");
        persons.Columns.Should().Equal("Name", "Role", "Associated");
        persons.Table.Should().HaveCount(2);
        persons.Table.Should().Contain(row => row[0] == "Dana Demo" && row[1] == "Suspect");

        document.Sections.Single(s => s.Heading == "Evidence summary")
            .Table.Should().HaveCount(2);
        document.Sections.Single(s => s.Heading == "Timeline summary")
            .Rows.Should().ContainSingle(row => row.Contains("Case opened"));

        // The card's section count is the document's non-empty section count.
        var listed = await service.ListAsync(new ReportFilterCriteria(), true);
        listed.Items.Single(i => i.Id == result.Value).SectionCount
            .Should().Be(document.Sections.Count(s => !s.IsEmpty));

        (await _context.AuditLogs.AnyAsync(l =>
            l.Action == AuditActionNames.ReportGenerated
            && l.EntityId == result.Value
            && l.UserId == _actorId)).Should().BeTrue();
    }

    [Fact]
    public async Task CaseReport_NeverExposesIdentifiersContactDataPrivateNotesOrEvidenceFiles()
    {
        var service = CreateService();
        var generated = await service.GenerateAsync(
            Generate(ReportType.CaseSummary, _focusCaseId), _actorId, true);
        var investigation = await service.GenerateAsync(
            Generate(ReportType.InvestigationProgress, _focusCaseId), _actorId, true);

        var caseText = Flatten((await service.GetDocumentAsync(generated.Value, _actorId, true))!);
        var investigationText = Flatten((await service.GetDocumentAsync(investigation.Value, _actorId, true))!);

        foreach (var text in new[] { caseText, investigationText })
        {
            text.Should().Contain("Dana Demo");
            text.Should().NotContain("NID-SECRET-9999");
            text.Should().NotContain("NID-SECRET-8888");
            text.Should().NotContain("555-0100");
            text.Should().NotContain("dana@private.example");
            text.Should().NotContain("12 Private Lane");
            text.Should().NotContain("raw-cctv-footage.mp4");
            text.Should().NotContain("deadbeefchecksum");
            text.Should().NotContain("informant payment details");
        }

        // A case summary carries no notes at all; the investigation summary shows only
        // the non-private note.
        caseText.Should().NotContain("CCTV reviewed with the station");
        investigationText.Should().Contain("Public observation: CCTV reviewed with the station.");
    }

    [Fact]
    public async Task CaseReport_ListsLinkedCasesWithAnAssociationCaveat()
    {
        var service = CreateService();
        var generated = await service.GenerateAsync(
            Generate(ReportType.CaseSummary, _focusCaseId), _actorId, true);

        var document = await service.GetDocumentAsync(generated.Value, _actorId, true);

        document!.LinkedCases.Should().ContainSingle(l =>
            l.CaseNumber == "RPT-2026-0102" && l.Id == _linkedCaseId);
        document.LinkedCases[0].Status.Should().Contain("Dana Demo");
        document.Findings.Should().Contain(f =>
            f.Contains("not a finding of involvement") && f.Contains("share one or more recorded"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("case")]
    public async Task Generate_CaseSummary_WithoutAUsableCase_IsRejected(string? mode)
    {
        var service = CreateService();
        Guid? caseId = mode == null ? null : Guid.NewGuid();

        var result = await service.GenerateAsync(Generate(ReportType.CaseSummary, caseId), _actorId, true);

        result.Status.Should().Be(OperationStatus.ValidationFailed);
        result.Errors.Keys.Should().Contain(nameof(GenerateReportRequest.CaseId));
        (await _context.Reports.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Generate_HandAuthoredType_IsRefusedWithAGuidingMessage()
    {
        var service = CreateService();

        var result = await service.GenerateAsync(Generate(ReportType.Custom), _actorId, true);

        result.Status.Should().Be(OperationStatus.ValidationFailed);
        result.Errors[nameof(GenerateReportRequest.Type)][0]
            .Should().Contain("Author it from the report editor instead");
    }

    [Fact]
    public async Task Generate_CaseScopedType_WithoutAnalyticalAuthorisation_IsAllowed()
    {
        var service = CreateService();

        var result = await service.GenerateAsync(
            Generate(ReportType.CaseSummary, _focusCaseId), _actorId, allowAnalytical: false);

        result.Succeeded.Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // Crime Trend Analysis (existing analytics service)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Generate_TrendReport_ReusesTheAnalyticsServiceNumbers()
    {
        var service = CreateService();
        var expected = await new AnalyticsService(_context, new FixedTimeProvider(FixedInstant))
            .GetReportAsync(new AnalyticsFilterCriteria
            {
                DateFrom = FixedNow.AddDays(-29).Date,
                DateTo = FixedNow.Date
            });

        var result = await service.GenerateAsync(Generate(ReportType.StatisticalAnalysis), _actorId, true);
        var document = await service.GetDocumentAsync(result.Value, _actorId, true);

        document!.Metrics.Single(m => m.Label == "Total cases").Value
            .Should().Be(expected.TotalCrimeVolume.ToString("N0"));
        document.Trend.Select(t => t.Count).Should().BeEquivalentTo(expected.MonthlyTrend.Select(t => t.Count));
        document.CrimeByType.Should().NotBeEmpty();
        document.SeverityDistribution.Should().NotBeEmpty();
        document.CrimeByDistrict.Select(d => d.DistrictName)
            .Should().BeEquivalentTo(expected.CrimeByDistrict.Select(d => d.DistrictName));

        // Decision-support wording, never a prediction.
        document.Findings.Should().Contain(f => f.StartsWith("Based on the available NETRA records", StringComparison.Ordinal));
        document.Findings.Should().Contain(f => f.Contains("not a forecast"));
    }

    [Fact]
    public async Task Generate_TrendReport_AppliesDistrictTypeAndSeverityFilters()
    {
        var service = CreateService();

        var theftOnly = await service.GenerateAsync(
            Generate(ReportType.StatisticalAnalysis, crimeTypeId: _theftId), _actorId, true);
        var centralHigh = await service.GenerateAsync(
            Generate(ReportType.StatisticalAnalysis, districtId: _centralId, severity: "High"), _actorId, true);
        var from1990 = await service.GenerateAsync(
            Generate(ReportType.StatisticalAnalysis, from: new DateTime(1990, 1, 1), to: new DateTime(1990, 1, 2)),
            _actorId, true);

        var theftDocument = await service.GetDocumentAsync(theftOnly.Value, _actorId, true);
        var centralDocument = await service.GetDocumentAsync(centralHigh.Value, _actorId, true);
        var emptyDocument = await service.GetDocumentAsync(from1990.Value, _actorId, true);

        theftDocument!.CrimeByType.Should().ContainSingle().Which.Label.Should().Be("Theft");
        centralDocument!.AppliedFilters.Should().Contain(
            "District Central District", "Severity High");
        centralDocument.Metrics.Single(m => m.Label == "Total cases").Value.Should().Be("1");

        // Empty period: honest zero, no invented rows.
        emptyDocument!.HasData.Should().BeFalse();
        emptyDocument.Metrics.Single(m => m.Label == "Total cases").Value.Should().Be("0");
        emptyDocument.Trend.Sum(t => t.Count).Should().Be(0);
        Flatten(emptyDocument).Should().NotContain("RPT-2026");
        var storedEmpty = await _context.Reports.FirstAsync(r => r.Id == from1990.Value);
        storedEmpty.Description.Should().Contain("no findings");
    }

    [Fact]
    public async Task GeneratedReport_ReadsLiveRecordsRatherThanASnapshottedCopy()
    {
        var service = CreateService();
        var result = await service.GenerateAsync(Generate(ReportType.StatisticalAnalysis), _actorId, true);
        var stored = await _context.Reports.FirstAsync(r => r.Id == result.Value);

        var before = (await service.GetDocumentAsync(result.Value, _actorId, true))!;
        var beforeTotal = int.Parse(before.Metrics.Single(m => m.Label == "Total cases").Value);

        // No data copy is persisted: only parameters, period and derived labels.
        stored.Parameters.Should().Contain("dateFrom").And.Contain("districtId");
        stored.Parameters!.Length.Should().BeLessThan(1000);
        stored.GeneratedFileSizeBytes.Should().BeNull();

        await AddCaseAsync("RPT-2026-0200", FixedNow.AddDays(-1), _theftId, _centralId);

        var after = (await service.GetDocumentAsync(result.Value, _actorId, true))!;
        int.Parse(after.Metrics.Single(m => m.Label == "Total cases").Value).Should().Be(beforeTotal + 1);
    }

    // ------------------------------------------------------------------
    // Risk Assessment (existing risk service)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Generate_RiskReport_UsesTheExistingRiskServiceAndLabelsIndicators()
    {
        var service = CreateService();
        var time = new FixedTimeProvider(FixedInstant);
        var expected = await new RiskAnalysisService(
            _context, new AnomalyDetector(), time, new StubActorAccessor(_actorId), Audit())
            .GetAnalysisAsync(new AnalyticsFilterCriteria
            {
                DateFrom = FixedNow.AddDays(-29).Date,
                DateTo = FixedNow.Date
            });

        var result = await service.GenerateAsync(Generate(ReportType.RiskAssessment), _actorId, true);
        var document = await service.GetDocumentAsync(result.Value, _actorId, true);

        document!.Metrics.Single(m => m.Label == "Cases in window").Value
            .Should().Be(expected.TotalCasesInWindow.ToString("N0"));
        var indicators = document.Sections.Single(s => s.Heading == "District indicators");
        indicators.Table.Should().HaveCount(expected.Indicators.Count);
        indicators.Table.Should().Contain(row => row.Contains("Central District"));

        // The identical formula is presented, never a second one, and always as an indicator.
        document.Findings.Should().Contain(f =>
            f.StartsWith("Analytical indicator —", StringComparison.Ordinal)
            && f.Contains("of 100"));
        document.Findings.Should().Contain(f =>
            f.Contains("not predictions of future crime"));
        Flatten(document).Should().NotContain("guaranteed");
    }

    [Fact]
    public async Task Generate_RiskReport_InsufficientDataIsStatedNotScored()
    {
        var service = CreateService();

        // A window with no cases: the risk service reports no scored districts.
        var result = await service.GenerateAsync(
            Generate(ReportType.RiskAssessment, from: new DateTime(1990, 1, 1), to: new DateTime(1990, 1, 2)),
            _actorId, true);
        var document = await service.GetDocumentAsync(result.Value, _actorId, true);

        document!.HasData.Should().BeFalse();
        document.Metrics.Single(m => m.Label == "Cases in window").Value.Should().Be("0");
        document.Metrics.Single(m => m.Label == "Districts scored").Value.Should().Be("0");
        document.Findings.Should().Contain(f => f.Contains("analytical indicators"));
        Flatten(document).Should().NotContain("RPT-2026");
    }

    // ------------------------------------------------------------------
    // Executive Intelligence Summary
    // ------------------------------------------------------------------

    [Fact]
    public async Task Generate_ExecutiveSummary_CombinesDashboardAnalyticsRiskAndAlerts()
    {
        var service = CreateService();

        var result = await service.GenerateAsync(Generate(ReportType.IntelligenceBrief), _actorId, true);
        var document = await service.GetDocumentAsync(result.Value, _actorId, true);

        document!.Sections.Select(s => s.Heading).Should().Contain(new[]
        {
            "Period position", "District comparison", "Analytical indicators",
            "Active and high-severity alerts", "Recent alerts of record"
        });
        document.Sections.Single(s => s.Heading == "Active and high-severity alerts")
            .Table.Should().Contain(row => row.Contains("RPT seed volume spike"));
        document.Metrics.Select(m => m.Label).Should().Contain(new[]
        {
            "Cases in period", "Active cases", "Active alerts", "Anomalies"
        });

        // Decision support only: no autonomous recommendation.
        document.Findings.Should().Contain(f => f.Contains("does not direct operational action"));
        document.Findings.Should().Contain(f => f.Contains("review by authorised personnel is recommended")
            || f.Contains("No district currently scores"));
        document.HasData.Should().BeTrue();
    }

    [Fact]
    public async Task Generate_AnalyticalType_WithoutAuthorisation_IsForbiddenAndPersistsNothing()
    {
        var service = CreateService();

        var trend = await service.GenerateAsync(
            Generate(ReportType.StatisticalAnalysis), _actorId, allowAnalytical: false);
        var risk = await service.GenerateAsync(
            Generate(ReportType.RiskAssessment), _actorId, allowAnalytical: false);
        var brief = await service.GenerateAsync(
            Generate(ReportType.IntelligenceBrief), _actorId, allowAnalytical: false);

        foreach (var result in new[] { trend, risk, brief })
        {
            result.Status.Should().Be(OperationStatus.Forbidden);
            result.Errors.Values.SelectMany(e => e).Single()
                .Should().Contain("restricted to authorised analytical roles");
        }

        (await _context.Reports.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Generate_OversizedObservations_FailValidation()
    {
        var service = CreateService();

        var result = await service.GenerateAsync(
            Generate(ReportType.StatisticalAnalysis,
                observations: new string('x', ReportLimits.MaxContentLength + 1)), _actorId, true);

        result.Status.Should().Be(OperationStatus.ValidationFailed);
        result.Errors.Keys.Should().Contain(nameof(GenerateReportRequest.AnalystObservations));
    }

    [Fact]
    public async Task Generate_KeepsAnalystObservationsSeparateFromSystemFacts()
    {
        var service = CreateService();

        var result = await service.GenerateAsync(
            Generate(ReportType.StatisticalAnalysis, observations: "Local festival may explain the June increase."),
            _actorId, true);

        var stored = await _context.Reports.FirstAsync(r => r.Id == result.Value);
        stored.Content.Should().Be("Local festival may explain the June increase.");
        stored.Source.Should().Be(ReportSource.SystemGenerated);

        var document = await service.GetDocumentAsync(result.Value, _actorId, true);
        document!.AnalystContent.Should().Be("Local festival may explain the June increase.");

        // Editing is refused for the generated facts, even though observations exist.
        var edit = await service.EditAsync(result.Value,
            new EditAnalystReportRequest { Title = "Rewritten headline" }, _actorId);
        edit.Status.Should().Be(OperationStatus.Conflict);
    }

    [Fact]
    public async Task Generate_WithoutDates_UsesTheBoundedDefaultPeriod()
    {
        var service = CreateService();

        var result = await service.GenerateAsync(Generate(ReportType.RiskAssessment), _actorId, true);
        var stored = await _context.Reports.FirstAsync(r => r.Id == result.Value);

        stored.PeriodTo!.Value.Date.Should().Be(FixedNow.Date);
        (stored.PeriodTo!.Value - stored.PeriodFrom!.Value).TotalDays
            .Should().Be(ReportLimits.DefaultPeriodDays - 1);
    }

    [Fact]
    public async Task Generate_AbsurdOrReversedPeriod_IsNormalisedAndClamped()
    {
        var service = CreateService();

        var tooLong = await service.GenerateAsync(
            Generate(ReportType.StatisticalAnalysis, from: new DateTime(1950, 1, 1)), _actorId, true);
        var reversed = await service.GenerateAsync(
            Generate(ReportType.StatisticalAnalysis,
                from: new DateTime(2026, 6, 14), to: new DateTime(2026, 5, 1)), _actorId, true);

        var longStored = await _context.Reports.FirstAsync(r => r.Id == tooLong.Value);
        longStored.PeriodTo!.Value.Date.Should().Be(FixedNow.Date);
        longStored.PeriodFrom!.Value.Date
            .Should().Be(FixedNow.Date.AddDays(-ReportLimits.MaxPeriodDays));

        var reversedStored = await _context.Reports.FirstAsync(r => r.Id == reversed.Value);
        // A reversed period is swapped, not rejected with an SQL date error.
        reversedStored.PeriodFrom!.Value.Date.Should().Be(new DateTime(2026, 5, 1));
        reversedStored.PeriodTo!.Value.Date.Should().Be(new DateTime(2026, 6, 14));
    }

    // ------------------------------------------------------------------
    // List, search, filters, pagination
    // ------------------------------------------------------------------

    [Fact]
    public async Task List_WithoutAnalyticalAccess_HidesAnalyticalReportsEntirely()
    {
        SeedReport("Trend for the library", ReportType.StatisticalAnalysis,
            createdAt: FixedNow.AddDays(-1));
        SeedReport("Risk for the library", ReportType.RiskAssessment, createdAt: FixedNow.AddDays(-2));
        SeedReport("Briefing for the library", ReportType.IntelligenceBrief, createdAt: FixedNow.AddDays(-3));
        SeedReport("Case summary for the library", ReportType.CaseSummary,
            createdAt: FixedNow.AddDays(-4), caseId: _focusCaseId);
        SeedReport("Analyst note for the library", ReportType.Custom, ReportSource.AnalystAuthored,
            ReportStatus.Draft, FixedNow.AddDays(-5));
        await _context.SaveChangesAsync();
        var service = CreateService();

        var full = await service.ListAsync(new ReportFilterCriteria(), true);
        var restricted = await service.ListAsync(new ReportFilterCriteria(), false);

        full.Items.Should().HaveCount(5);
        restricted.Items.Should().HaveCount(2);
        restricted.Items.Select(i => i.Title).Should().BeEquivalentTo(
            "Case summary for the library", "Analyst note for the library");
        // Existence is not even hinted at through the counters.
        (await service.GetSummaryAsync(true)).TotalCount.Should().Be(5);
        var restrictedSummary = await service.GetSummaryAsync(false);
        restrictedSummary.TotalCount.Should().Be(2);
        restrictedSummary.AnalyticalCount.Should().Be(0);
    }

    [Fact]
    public async Task List_SearchMatchesTitleSummaryAndAuthorName()
    {
        SeedReport("Quarterly harbour review", ReportType.Custom, ReportSource.AnalystAuthored,
            createdAt: FixedNow.AddDays(-1));
        SeedReport("Unrelated title", ReportType.Custom, ReportSource.AnalystAuthored,
            summary: "Mentions the harbour authority in the body.", createdAt: FixedNow.AddDays(-2));
        SeedReport("Another title", ReportType.Custom, ReportSource.AnalystAuthored,
            createdAt: FixedNow.AddDays(-3), authorId: _secondUserId);
        await _context.SaveChangesAsync();
        var service = CreateService();

        var byTitle = await service.ListAsync(new ReportFilterCriteria { Search = "harbour review" }, true);
        var bySummary = await service.ListAsync(new ReportFilterCriteria { Search = "harbour authority" }, true);
        var byAuthor = await service.ListAsync(new ReportFilterCriteria { Search = "Omar" }, true);
        var none = await service.ListAsync(new ReportFilterCriteria { Search = "no-such-term" }, true);

        byTitle.Items.Should().ContainSingle().Which.Title.Should().Be("Quarterly harbour review");
        bySummary.Items.Should().ContainSingle().Which.Title.Should().Be("Unrelated title");
        byAuthor.Items.Should().ContainSingle().Which.AuthorName.Should().Be("Omar Officer");
        none.Items.Should().BeEmpty();
        none.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task List_PagesStablyAndClampsThePageSize()
    {
        for (var i = 1; i <= 7; i++)
        {
            SeedReport($"Paged report {i:D2}", ReportType.Custom, ReportSource.AnalystAuthored,
                createdAt: FixedNow.AddDays(-i));
        }

        await _context.SaveChangesAsync();
        var service = CreateService();

        var firstPage = await service.ListAsync(new ReportFilterCriteria { PageSize = 3, Page = 1 }, true);
        var secondPage = await service.ListAsync(new ReportFilterCriteria { PageSize = 3, Page = 2 }, true);
        var clamped = await service.ListAsync(
            new ReportFilterCriteria { PageSize = 5000, Page = 0 }, true);

        firstPage.TotalCount.Should().Be(7);
        firstPage.TotalPages.Should().Be(3);
        firstPage.Items.Select(i => i.Title).Should().Equal(
            "Paged report 01", "Paged report 02", "Paged report 03");
        secondPage.Items.Select(i => i.Title).Should().Equal(
            "Paged report 04", "Paged report 05", "Paged report 06");
        firstPage.Items.Select(i => i.Id).Intersect(secondPage.Items.Select(i => i.Id))
            .Should().BeEmpty();
        clamped.PageSize.Should().Be(ReportLimits.MaxPageSize);
        clamped.Page.Should().Be(1);

        // Newest first and each row carries the derived labels the UI shows.
        var item = firstPage.Items[0];
        item.CreatedAt.Should().Be(FixedNow.AddDays(-1));
        item.PeriodBadge.Should().Be("17 May – 15 Jun 2026");
        item.PeriodLabel.Should().Be("17 May 2026 – 15 Jun 2026");
        item.CanEdit.Should().BeTrue();
        item.AuthorName.Should().Be("Ada Analyst");
    }

    [Theory]
    [InlineData(ReportGroup.TrendAnalysis, "Trend row")]
    [InlineData(ReportGroup.RiskAssessments, "Risk row")]
    [InlineData(ReportGroup.Investigations, "Case row|Investigation row")]
    public async Task List_GroupTabsRestrictToTheirMappedTypes(ReportGroup group, string expectedTitles)
    {
        SeedReport("Trend row", ReportType.StatisticalAnalysis, createdAt: FixedNow.AddDays(-1));
        SeedReport("Risk row", ReportType.RiskAssessment, createdAt: FixedNow.AddDays(-2));
        SeedReport("Case row", ReportType.CaseSummary, createdAt: FixedNow.AddDays(-3), caseId: _focusCaseId);
        SeedReport("Investigation row", ReportType.InvestigationProgress,
            createdAt: FixedNow.AddDays(-4), caseId: _focusCaseId);
        SeedReport("Briefing row", ReportType.IntelligenceBrief, createdAt: FixedNow.AddDays(-5));
        await _context.SaveChangesAsync();
        var service = CreateService();

        var filtered = await service.ListAsync(new ReportFilterCriteria { Group = group }, true);

        // Each tab shows exactly its mapped report types — the intelligence brief is in none of them.
        filtered.Items.Select(i => i.Title).Should().BeEquivalentTo(expectedTitles.Split('|'));
        filtered.TotalCount.Should().Be(filtered.Items.Count);
    }

    [Fact]
    public async Task List_SourceAndStatusFiltersNarrowTheLibrary()
    {
        SeedReport("Generated ready", ReportType.CaseSummary, ReportSource.SystemGenerated,
            ReportStatus.Ready, FixedNow.AddDays(-1), _focusCaseId);
        SeedReport("Authored draft", ReportType.Custom, ReportSource.AnalystAuthored,
            ReportStatus.Draft, FixedNow.AddDays(-2));
        SeedReport("Authored archived", ReportType.Custom, ReportSource.AnalystAuthored,
            ReportStatus.Archived, FixedNow.AddDays(-3));
        await _context.SaveChangesAsync();
        var service = CreateService();

        var drafts = await service.ListAsync(
            new ReportFilterCriteria { Source = ReportSource.AnalystAuthored, Status = ReportStatus.Draft }, true);
        var archived = await service.ListAsync(
            new ReportFilterCriteria { Status = ReportStatus.Archived }, true);
        var byType = await service.ListAsync(
            new ReportFilterCriteria { Type = ReportType.CaseSummary }, true);

        drafts.Items.Should().ContainSingle().Which.Title.Should().Be("Authored draft");
        drafts.Items[0].CanEdit.Should().BeTrue("an analyst may edit their own draft");
        archived.Items.Should().ContainSingle().Which.Title.Should().Be("Authored archived");
        archived.Items[0].CanEdit.Should().BeFalse("an archived report is out of the working set");
        byType.Items.Should().ContainSingle().Which.CaseNumber.Should().Be("RPT-2026-0101");
        byType.Items[0].CanEdit.Should().BeFalse("system-generated rows are never editable");
        (await service.GetSummaryAsync(true)).ArchivedCount.Should().Be(1);
    }

    [Fact]
    public async Task List_EmptyLibrary_ReturnsNoRowsRatherThanPlaceholders()
    {
        var service = CreateService();

        var paged = await service.ListAsync(new ReportFilterCriteria(), true);
        var summary = await service.GetSummaryAsync(true);

        paged.Items.Should().BeEmpty();
        paged.TotalCount.Should().Be(0);
        summary.TotalCount.Should().Be(0);
        summary.SystemGeneratedCount.Should().Be(0);
        summary.AnalystAuthoredCount.Should().Be(0);
    }

    [Fact]
    public async Task GetCaseOptions_IsBoundedAndNumbered()
    {
        var service = CreateService();

        var options = await service.GetCaseOptionsAsync();

        options.Should().HaveCount(6);
        options.Select(o => o.CaseNumber).Should().OnlyContain(n => n.StartsWith("RPT-", StringComparison.Ordinal));
        options.First().CaseNumber.Should().Be("RPT-2026-0104");
    }

    // ------------------------------------------------------------------
    // Document access and audit
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetDocument_UnknownId_ReturnsNull()
    {
        var service = CreateService();

        (await service.GetDocumentAsync(Guid.NewGuid(), _actorId, true)).Should().BeNull();
    }

    [Fact]
    public async Task GetDocument_AnalyticalReportWithoutAccess_AnswersNotFoundAndAuditsTheDenial()
    {
        SeedReport("Confidential trend", ReportType.StatisticalAnalysis, createdAt: FixedNow);
        await _context.SaveChangesAsync();
        var reportId = (await _context.Reports.FirstAsync(r => r.Title == "Confidential trend")).Id;
        var service = CreateService();

        var denied = await service.GetDocumentAsync(reportId, _secondUserId, allowAnalytical: false);

        denied.Should().BeNull();
        var reference = reportId.ToString("N");
        var denials = await _context.AuditLogs
            .Where(l => l.Action == AuditActionNames.ReportAccessDenied && l.UserId == _secondUserId)
            .ToListAsync();
        denials.Should().ContainSingle(l => l.NewValues!.Contains(reference, StringComparison.Ordinal));
        denials.Single().EntityType.Should().Be(AuditEntityTypeNames.Account,
            "a denial is a security event against the actor, never against the report");
        (await _context.AuditLogs.AnyAsync(l =>
            l.EntityId == reportId && l.Action == AuditActionNames.ReportViewed)).Should().BeFalse();

        // The same report opens normally for an analytical reader, and the read is audited.
        var opened = await service.GetDocumentAsync(reportId, _actorId, allowAnalytical: true);
        opened.Should().NotBeNull();
        (await _context.AuditLogs.AnyAsync(l =>
            l.Action == AuditActionNames.ReportViewed
            && l.EntityId == reportId
            && l.UserId == _actorId)).Should().BeTrue();
    }

    [Fact]
    public async Task GetDocument_CorruptParameterBlob_StillRendersFromStoredFields()
    {
        var report = SeedReport("Legacy report", ReportType.Custom, ReportSource.AnalystAuthored,
            ReportStatus.Ready, FixedNow, summary: "Written before parameters existed.");
        report.Parameters = "{not json";
        await _context.SaveChangesAsync();
        var service = CreateService();

        var document = await service.GetDocumentAsync(report.Id, _actorId, true);

        document!.Title.Should().Be("Legacy report");
        document.Sections.Should().ContainSingle(s => s.Heading == "Summary");
        document.HasData.Should().BeTrue();
    }

    [Fact]
    public async Task GetDocument_AuthoredReportWithoutContent_IsMarkedAsHavingNoData()
    {
        var report = SeedReport("Empty briefing", ReportType.Custom, ReportSource.AnalystAuthored,
            ReportStatus.Draft, FixedNow);
        report.Description = null;
        await _context.SaveChangesAsync();
        var service = CreateService();

        var document = await service.GetDocumentAsync(report.Id, _actorId, true);

        // The UI needs this signal to render an honest empty state instead of a blank page.
        document!.HasData.Should().BeFalse();
        document.Sections.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // Analyst authoring
    // ------------------------------------------------------------------

    [Fact]
    public async Task Create_ValidBriefing_PersistsAsEditableDraftAndAudits()
    {
        var service = CreateService();

        var result = await service.CreateAsync(new CreateAnalystReportRequest
        {
            Title = "Night patrol observations",
            Type = ReportType.Custom,
            CaseId = _focusCaseId,
            Summary = "Observed increase in night-time theft reports.",
            Content = "Street lighting review recommended by authorised personnel.",
            PeriodFrom = FixedNow.AddDays(-14),
            PeriodTo = FixedNow
        }, _actorId);

        result.Succeeded.Should().BeTrue();
        var stored = await _context.Reports.FirstAsync(r => r.Id == result.Value);
        stored.Source.Should().Be(ReportSource.AnalystAuthored);
        stored.Status.Should().Be(ReportStatus.Draft);
        stored.ReportType.Should().Be(ReportType.Custom);
        stored.CreatedAt.Should().Be(FixedNow);

        var document = await service.GetDocumentAsync(result.Value, _actorId, true);
        document!.AnalystContent.Should().Be("Street lighting review recommended by authorised personnel.");
        document.Sections.Should().Contain(s => s.Heading == "Analyst observations");
        (await _context.AuditLogs.AnyAsync(l =>
            l.Action == AuditActionNames.ReportCreated && l.EntityId == result.Value)).Should().BeTrue();
    }

    [Theory]
    [InlineData("", null, null, null, "title")]
    [InlineData("short", null, null, null, "title")]
    [InlineData("A long enough title", "Analytical", null, null, "Type")]
    [InlineData("A long enough title", null, "2030-01-05", "2030-01-01", "periodFrom")]
    public async Task Create_InvalidRequests_AreRefusedServerSide(
        string title, string? typeName, string? from, string? to, string expectedKey)
    {
        var service = CreateService();
        var type = typeName == "Analytical" ? ReportType.CaseSummary : ReportType.Custom;

        var result = await service.CreateAsync(new CreateAnalystReportRequest
        {
            Title = title,
            Type = type,
            Summary = "Summary",
            PeriodFrom = from is null ? null : DateTime.Parse(from),
            PeriodTo = to is null ? null : DateTime.Parse(to)
        }, _actorId);

        result.Status.Should().Be(OperationStatus.ValidationFailed);
        result.Errors.Keys.Should().Contain(expectedKey);
        (await _context.Reports.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Create_KnownFieldLimitsAndUnknownCase_AreValidated()
    {
        var service = CreateService();

        var oversizedTitle = await service.CreateAsync(new CreateAnalystReportRequest
        {
            Title = new string('t', ReportLimits.MaxTitleLength + 1),
            Type = ReportType.Custom
        }, _actorId);
        var unknownCase = await service.CreateAsync(new CreateAnalystReportRequest
        {
            Title = "Valid title",
            Type = ReportType.Custom,
            CaseId = Guid.NewGuid()
        }, _actorId);

        oversizedTitle.Status.Should().Be(OperationStatus.ValidationFailed);
        oversizedTitle.Errors["title"][0].Should().Contain("200 characters or fewer");
        unknownCase.Status.Should().Be(OperationStatus.ValidationFailed);
        unknownCase.Errors.Keys.Should().Contain(nameof(CreateAnalystReportRequest.CaseId));
    }

    [Fact]
    public async Task Create_GeneratedOnlyType_IsRefusedSoSystemFactsCannotBeForged()
    {
        var service = CreateService();

        var result = await service.CreateAsync(new CreateAnalystReportRequest
        {
            Title = "Handwritten case summary",
            Type = ReportType.CaseSummary
        }, _actorId);

        result.Status.Should().Be(OperationStatus.ValidationFailed);
        result.Errors[nameof(CreateAnalystReportRequest.Type)][0]
            .Should().Contain("produced by generation, not authored by hand");
    }

    [Fact]
    public async Task Edit_AnalystReport_UpdatesContentAndPromotesDraftToReady()
    {
        var service = CreateService();
        var created = await service.CreateAsync(new CreateAnalystReportRequest
        {
            Title = "Initial briefing title",
            Type = ReportType.Custom,
            Summary = "Initial summary",
            Content = "Initial observations"
        }, _actorId);

        var edited = await service.EditAsync(created.Value, new EditAnalystReportRequest
        {
            Title = "Revised briefing title",
            Summary = "Revised summary",
            Content = "Revised observations"
        }, _secondUserId);

        edited.Succeeded.Should().BeTrue();
        var stored = await _context.Reports.FirstAsync(r => r.Id == created.Value);
        stored.Title.Should().Be("Revised briefing title");
        stored.Content.Should().Be("Revised observations");
        stored.Status.Should().Be(ReportStatus.Ready);
        stored.Source.Should().Be(ReportSource.AnalystAuthored);
        (await _context.AuditLogs.AnyAsync(l =>
            l.Action == AuditActionNames.ReportEdited
            && l.EntityId == created.Value
            && l.UserId == _secondUserId
            && l.PropertyName == nameof(Report.Title))).Should().BeTrue();
    }

    [Fact]
    public async Task Edit_SystemGeneratedOrArchived_IsRefused()
    {
        var service = CreateService();
        var generated = await service.GenerateAsync(
            Generate(ReportType.CaseSummary, _focusCaseId), _actorId, true);
        SeedReport("Archived briefing", ReportType.Custom, ReportSource.AnalystAuthored,
            ReportStatus.Archived, FixedNow);
        await _context.SaveChangesAsync();
        var archivedId = (await _context.Reports.FirstAsync(r => r.Title == "Archived briefing")).Id;

        var immutable = await service.EditAsync(generated.Value,
            new EditAnalystReportRequest { Title = "Overwriting system facts" }, _actorId);
        var archived = await service.EditAsync(archivedId,
            new EditAnalystReportRequest { Title = "Overwriting an archive" }, _actorId);
        var missing = await service.EditAsync(Guid.NewGuid(),
            new EditAnalystReportRequest { Title = "No such report" }, _actorId);

        immutable.Status.Should().Be(OperationStatus.Conflict);
        immutable.Errors.Values.SelectMany(e => e).Single()
            .Should().Contain("System-generated facts are immutable");
        archived.Status.Should().Be(OperationStatus.Conflict);
        archived.Errors.Values.SelectMany(e => e).Single().Should().Contain("Archived reports cannot be edited");
        missing.Status.Should().Be(OperationStatus.NotFound);
        (await _context.Reports.FirstAsync(r => r.Id == generated.Value)).Title
            .Should().Be("Case Summary — RPT-2026-0101");
    }

    [Fact]
    public async Task Edit_InvalidTitle_LeavesTheStoredReportUntouched()
    {
        var service = CreateService();
        var created = await service.CreateAsync(new CreateAnalystReportRequest
        {
            Title = "Carefully chosen title",
            Type = ReportType.Custom
        }, _actorId);

        var result = await service.EditAsync(created.Value,
            new EditAnalystReportRequest { Title = "abc" }, _actorId);

        result.Status.Should().Be(OperationStatus.ValidationFailed);
        (await _context.Reports.FirstAsync(r => r.Id == created.Value)).Title
            .Should().Be("Carefully chosen title");
    }

    [Fact]
    public async Task Archive_SetsStatusOnceAndKeepsTheReportReadable()
    {
        var service = CreateService();
        var created = await service.CreateAsync(new CreateAnalystReportRequest
        {
            Title = "Briefing to retire",
            Type = ReportType.Custom
        }, _actorId);

        var archived = await service.ArchiveAsync(created.Value, _actorId);
        var twice = await service.ArchiveAsync(created.Value, _actorId);
        var missing = await service.ArchiveAsync(Guid.NewGuid(), _actorId);

        archived.Succeeded.Should().BeTrue();
        twice.Status.Should().Be(OperationStatus.Conflict);
        missing.Status.Should().Be(OperationStatus.NotFound);

        var stored = await _context.Reports.FirstAsync(r => r.Id == created.Value);
        stored.Status.Should().Be(ReportStatus.Archived);
        stored.ArchivedAt.Should().Be(FixedNow);
        (await service.GetDocumentAsync(created.Value, _actorId, true))!.Status
            .Should().Be(ReportStatus.Archived);
        (await _context.AuditLogs.AnyAsync(l =>
            l.Action == AuditActionNames.ReportArchived
            && l.EntityId == created.Value
            && l.NewValues == "Archived"
            && l.OldValues == "Draft")).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // CSV export (server-side, no client library)
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExportCsv_WritesTheSameDocumentThePageShowsAndAuditsIt()
    {
        var service = CreateService();
        var generated = await service.GenerateAsync(
            Generate(ReportType.CaseSummary, _focusCaseId), _actorId, true);

        var export = await service.ExportCsvAsync(generated.Value, _actorId, true);

        export.Should().NotBeNull();
        var csv = System.Text.Encoding.UTF8.GetString(export!.Value.Content);
        export.Value.FileName.Should().MatchRegex("^casesummaryrpt20260101-[0-9a-f]{32}\\.csv$");

        csv.Should().Contain("NETRA report export");
        csv.Should().Contain("Case Summary — RPT-2026-0101");
        csv.Should().Contain("Report type,Case Summary");
        csv.Should().Contain("Source,NETRA System Generated");
        csv.Should().Contain("Persons of interest");
        csv.Should().Contain("Dana Demo,Suspect");
        csv.Should().Contain("Disclaimer");
        csv.Should().Contain("not a prediction");
        csv.Should().NotContain("NID-SECRET-9999");
        csv.Should().NotContain("raw-cctv-footage.mp4");
        (await _context.AuditLogs.AnyAsync(l =>
            l.Action == AuditActionNames.ReportExported && l.EntityId == generated.Value)).Should().BeTrue();
    }

    [Fact]
    public async Task ExportCsv_NeutralisesSpreadsheetFormulasInAuthoredText()
    {
        var service = CreateService();
        var created = await service.CreateAsync(new CreateAnalystReportRequest
        {
            Title = "Formula probe briefing",
            Type = ReportType.Custom,
            Summary = "=HYPERLINK(\"http://evil.example\",\"click\")",
            Content = "+CMD(\"calc\")\r\n-2+3\r\n@SUM(A1:A9)"
        }, _actorId);

        var export = await service.ExportCsvAsync(created.Value, _actorId, true);
        var csv = System.Text.Encoding.UTF8.GetString(export!.Value.Content);

        csv.Should().Contain("'=HYPERLINK");
        csv.Should().Contain("'+CMD");
        csv.Should().Contain("'-2+3");
        csv.Should().Contain("'@SUM");
        csv.Should().NotContain(",,\"=HYPERLINK");
    }

    [Fact]
    public async Task ExportCsv_UnauthorisedAnalyticalReport_ReturnsNullAndAuditsOnlyTheDenial()
    {
        SeedReport("Restricted trend export", ReportType.StatisticalAnalysis, createdAt: FixedNow);
        await _context.SaveChangesAsync();
        var reportId = (await _context.Reports.FirstAsync(r => r.Title == "Restricted trend export")).Id;
        var service = CreateService();

        (await service.ExportCsvAsync(reportId, _secondUserId, allowAnalytical: false)).Should().BeNull();

        (await _context.AuditLogs.AnyAsync(l =>
            l.Action == AuditActionNames.ReportExported && l.EntityId == reportId)).Should().BeFalse();
        (await _context.AuditLogs.AnyAsync(l =>
            l.Action == AuditActionNames.ReportAccessDenied && l.UserId == _secondUserId)).Should().BeTrue();
    }

    [Fact]
    public async Task ExportCsv_EmptyTrendReport_SaysSoInsteadOfWritingInventedFigures()
    {
        var service = CreateService();
        var generated = await service.GenerateAsync(
            Generate(ReportType.StatisticalAnalysis,
                from: new DateTime(1990, 1, 1), to: new DateTime(1990, 1, 2)), _actorId, true);

        var export = await service.ExportCsvAsync(generated.Value, _actorId, true);
        var csv = System.Text.Encoding.UTF8.GetString(export!.Value.Content);

        csv.Should().Contain("Total cases,0");
        csv.Should().Contain("No cases match the selected reporting period");
        csv.Should().NotContain("RPT-2026");
    }
}
