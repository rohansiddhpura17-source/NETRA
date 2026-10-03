using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NETRA.Application.Abstractions;
using NETRA.Application.BulkImport;
using NETRA.Domain.Cases;
using NETRA.Domain.Enums;
using NETRA.Domain.Geography;
using NETRA.Domain.Import;
using NETRA.Domain.Security;
using NETRA.Infrastructure.Data;
using NETRA.Infrastructure.Services;
using System.Text;
using Xunit;

namespace NETRA.Tests.Application;

/// <summary>
/// Service-level tests for <see cref="BulkImportService"/>: upload validation, staged
/// validation, read-only preview, the confirmed import and its duplicate handling,
/// ownership enforcement, processing ceilings and audit recording.
/// </summary>
/// <remarks>
/// These run on EF Core's in-memory provider with a real <see cref="FileStorageService"/>
/// rooted in a temporary directory. They are not live SQL Server tests. The in-memory
/// provider does not support transactions, so the assertions below prove the guarantee the
/// code actually relies on for that provider: the whole accepted set is written by a single
/// SaveChangesAsync, and an import that fails its pre-commit re-check writes nothing at all.
/// Fixture content is entirely synthetic.
/// </remarks>
public sealed class BulkImportServiceTests : IDisposable
{
    private static readonly DateTimeOffset FixedInstant = new(2026, 6, 15, 9, 30, 0, TimeSpan.Zero);

    private static readonly DateTime FixedNow = FixedInstant.UtcDateTime;

    private readonly NetraDbContext _context;
    private readonly string _storageRoot;
    private readonly FileStorageService _storage;
    private DateTimeOffset _instant = FixedInstant;
    private readonly Guid _actorId;
    private readonly Guid _otherUserId;
    private readonly Guid _centralDistrictId;
    private readonly Guid _northDistrictId;
    private readonly Guid _theftTypeId;
    private readonly Guid _assaultTypeId;
    private readonly Guid _centralStationId;
    private readonly Guid _northStationId;

    public BulkImportServiceTests()
    {
        _context = new NetraDbContext(
            new DbContextOptionsBuilder<NetraDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        _storageRoot = Path.Combine(Path.GetTempPath(), $"netra-import-tests-{Guid.NewGuid():N}");
        _storage = new FileStorageService(
            Options.Create(new FileStorageOptions
            {
                RootPath = _storageRoot,
                MaxFileSizeBytes = 25 * 1024 * 1024
            }),
            AppContext.BaseDirectory);

        var actor = NewUser("import.operator@netra.local", "Importa", "Operator");
        _actorId = actor.Id;

        var other = NewUser("import.other@netra.local", "Importo", "Other");
        _otherUserId = other.Id;

        _theftTypeId = Guid.NewGuid();
        _assaultTypeId = Guid.NewGuid();

        var theft = new CrimeType
        {
            Id = _theftTypeId, Name = "Theft", Code = "THF", DisplayOrder = 1, IsActive = true
        };
        var assault = new CrimeType
        {
            Id = _assaultTypeId, Name = "Assault", Code = "ASL", DisplayOrder = 2, IsActive = true
        };
        var retired = new CrimeType
        {
            Id = Guid.NewGuid(), Name = "Pickpocketing", Code = "PPK", DisplayOrder = 3, IsActive = false
        };

        _centralDistrictId = Guid.NewGuid();
        _northDistrictId = Guid.NewGuid();

        var central = new District { Id = _centralDistrictId, Name = "Central District", Code = "CD", IsActive = true };
        var north = new District { Id = _northDistrictId, Name = "North District", Code = "ND", IsActive = true };

        _centralStationId = Guid.NewGuid();
        _northStationId = Guid.NewGuid();

        var centralStation = new PoliceStation
        {
            Id = _centralStationId, Name = "Central HQ", Code = "CHQ", DistrictId = _centralDistrictId, IsActive = true
        };
        var northStation = new PoliceStation
        {
            Id = _northStationId, Name = "North HQ", Code = "SHQ", DistrictId = _northDistrictId, IsActive = true
        };

        _context.AddRange(actor, other, theft, assault, retired, central, north, centralStation, northStation);

        // One registered case, so "already exists in NETRA" is testable without a unique index.
        _context.CrimeCases.Add(new CrimeCase
        {
            CaseNumber = "BX-9202",
            Title = "Case already registered before this import",
            CrimeTypeId = _theftTypeId,
            Status = CaseStatus.Open,
            Severity = CaseSeverity.Medium,
            IncidentDate = FixedNow.AddDays(30),
            CreatedById = _actorId
        });

        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Dispose();
        if (Directory.Exists(_storageRoot))
        {
            Directory.Delete(_storageRoot, recursive: true);
        }
    }

    // ------------------------------------------------------------------------ test doubles

    /// <summary>
    /// A clock the tests can advance. Jobs stamped with the same instant would order
    /// arbitrarily, so the recency assertions move time forward deliberately.
    /// </summary>
    private sealed class SettableTimeProvider(Func<DateTimeOffset> now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now();
    }

    private sealed class StubActorAccessor : IAuditActorAccessor
    {
        private readonly Guid _id;

        public StubActorAccessor(Guid id) => _id = id;

        public Guid GetUserId() => _id;

        public Guid GetUserId(System.Security.Claims.ClaimsPrincipal? principal) => _id;
    }

    private BulkImportService CreateService() => CreateServiceAs(_actorId);

    private BulkImportService CreateServiceAs(Guid actorId) => new(
        _context,
        _storage,
        new AuditService(_context, new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            new StubActorAccessor(actorId)),
        new SettableTimeProvider(() => _instant));

    // The in-memory provider only assigns key values at SaveChanges, so identities used as
    // foreign keys must be set before the entity is added.
    private ApplicationUser NewUser(string email, string firstName, string lastName) => new()
    {
        Id = Guid.NewGuid(),
        UserName = email,
        Email = email,
        FirstName = firstName,
        LastName = lastName,
        EmailConfirmed = true,
        IsActive = true
    };

    // ----------------------------------------------------------------------------- fixtures

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "BulkImport", name));

    private static UploadBulkImportRequest Request(
        string csv,
        string name = "Synthetic batch",
        string fileName = "cases.csv",
        long? length = null)
    {
        var bytes = Encoding.UTF8.GetBytes(csv);

        return new UploadBulkImportRequest
        {
            Name = name,
            Description = "Created by automated tests from synthetic data.",
            OriginalFileName = fileName,
            ContentType = "text/csv",
            Length = length ?? bytes.LongLength,
            Content = new MemoryStream(bytes)
        };
    }

    private async Task<Guid> StageAsync(string csv, string name = "Synthetic batch", string fileName = "cases.csv")
    {
        var service = CreateService();
        var uploaded = await service.UploadAsync(Request(csv, name, fileName), _actorId);
        uploaded.Succeeded.Should().BeTrue(string.Join("; ", uploaded.Errors.SelectMany(e => e.Value)));
        return uploaded.Value!.Id;
    }

    // ------------------------------------------------------------------ upload: §7, §8, §21

    [Fact]
    public async Task Upload_StoresTheFileAndRegistersAJob_WithoutReadingAnyRow()
    {
        var service = CreateService();

        var result = await service.UploadAsync(Request(Fixture("valid.csv")), _actorId);

        result.Succeeded.Should().BeTrue();
        result.Value!.Status.Should().Be(ImportStatus.Pending);
        result.Value.TotalRows.Should().Be(0);
        result.Value.FileName.Should().Be("cases.csv");

        var job = await _context.ImportJobs.SingleAsync();
        job.SourceFilePath.Should().NotBeNullOrWhiteSpace();
        job.Status.Should().Be(ImportStatus.Pending);

        // Upload registers the file. Staging is validation's job, and cases are the
        // import's job, so neither table may hold a row yet.
        (await _context.ImportRecords.AnyAsync()).Should().BeFalse();
        (await _context.CrimeCases.CountAsync(c => c.CaseNumber.StartsWith("BX-"))).Should().Be(1);
    }

    [Fact]
    public async Task Upload_RejectsANonCsvExtension()
    {
        var service = CreateService();

        var result = await service.UploadAsync(
            Request("CaseNumber\nBX-1\n", fileName: "cases.txt"), _actorId);

        result.Succeeded.Should().BeFalse();
        result.Status.Should().Be(OperationStatus.ValidationFailed);
        result.Errors.Keys.Should().Contain(nameof(UploadBulkImportRequest.OriginalFileName));
        (await _context.ImportJobs.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Upload_RejectsAPathTraversalStyleFileName()
    {
        var service = CreateService();

        var result = await service.UploadAsync(
            Request("CaseNumber\nBX-1\n", fileName: "../../etc/passwd.csv"), _actorId);

        result.Succeeded.Should().BeFalse();
        result.Errors.Values.SelectMany(v => v).Single().Should().Contain("file name is not valid");
    }

    [Fact]
    public async Task Upload_RejectsAnEmptyFile()
    {
        var service = CreateService();

        var result = await service.UploadAsync(Request(Fixture("empty.csv")), _actorId);

        result.Succeeded.Should().BeFalse();
        (await _context.ImportJobs.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Upload_RejectsAFileOverTheImportSizeCeiling()
    {
        var service = CreateService();

        // The controller passes the form file's real length, so a declared oversize value is
        // refused before any byte of the upload is read into memory.
        var result = await service.UploadAsync(
            Request("CaseNumber\n", length: BulkImportLimits.MaxFileBytes + 1), _actorId);

        result.Succeeded.Should().BeFalse();
        result.Errors.Values.SelectMany(v => v).Single().Should().Contain("MB import limit");
    }

    [Fact]
    public async Task Upload_RejectsAnUnknownContentTypeName()
    {
        var service = CreateService();

        var request = Request("CaseNumber\nBX-1\n");
        request = new UploadBulkImportRequest
        {
            Name = request.Name,
            OriginalFileName = "cases.csv",
            ContentType = "application/x-sh",
            Length = request.Length,
            Content = new MemoryStream(Encoding.UTF8.GetBytes("CaseNumber\nBX-1\n"))
        };

        var result = await service.UploadAsync(request, _actorId);

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Upload_RejectsAnAccountThatIsNotInTheRegister()
    {
        var service = CreateService();

        var result = await service.UploadAsync(
            Request(Fixture("valid.csv")), Guid.NewGuid());

        result.Status.Should().Be(OperationStatus.Forbidden);
    }

    [Fact]
    public async Task Upload_AuditsTheUploadWithoutStoringFileContents()
    {
        await StageAsync(Fixture("valid.csv"));

        var entry = (await _context.AuditLogs
                .Where(l => l.Action == AuditActionNames.ImportUploaded)
                .ToListAsync())
            .Single();

        entry.EntityType.Should().Be(AuditEntityTypeNames.ImportJob);
        entry.UserId.Should().Be(_actorId);

        // The audit line names the file and hashes it. It never carries row content,
        // so a leaked log cannot leak case data.
        entry.NewValues.Should().NotContain("Synthetic warehouse theft");
        entry.NewValues.Should().NotContain("BX-9001");
        entry.NewValues.Should().Contain("cases.csv");
    }

    // ---------------------------------------------------------------------- validation: §7

    [Fact]
    public async Task Validate_AcceptsEveryRowOfTheValidFixture()
    {
        var jobId = await StageAsync(Fixture("valid.csv"));
        var service = CreateService();

        var result = await service.ValidateAsync(jobId, _actorId);

        result.Succeeded.Should().BeTrue(string.Join("; ", result.Errors.SelectMany(e => e.Value)));
        result.Value!.TotalRows.Should().Be(3);
        result.Value.ValidRows.Should().Be(3);
        result.Value.InvalidRows.Should().Be(0);
        result.Value.DuplicateRows.Should().Be(0);
        result.Value.Issues.Should().BeEmpty();
        result.Value.Job.Status.Should().Be(ImportStatus.Validated);
    }

    [Fact]
    public async Task Validate_FailsHonestlyWhenTheStoredFileHasBecomeEmpty()
    {
        var jobId = await StageAsync(Fixture("valid.csv"));

        // Upload refuses an empty file, so an empty stored file is only reachable by
        // tampering with storage. Validation must still report it instead of importing
        // an empty batch or throwing.
        var job = await _context.ImportJobs.SingleAsync(j => j.Id == jobId);
        File.WriteAllBytes(Path.Combine(_storageRoot, job.SourceFilePath), Array.Empty<byte>());

        var service = CreateService();
        var result = await service.ValidateAsync(jobId, _actorId);

        result.Succeeded.Should().BeFalse();
        (await _context.ImportRecords.AnyAsync()).Should().BeFalse();
        (await _context.ImportJobs.SingleAsync(j => j.Id == jobId)).Status
            .Should().Be(ImportStatus.Failed);
    }

    [Fact]
    public async Task Validate_RejectsAHeaderOnlyFile()
    {
        var jobId = await StageAsync(
            "CaseNumber,Title,CrimeType,IncidentDate,Severity,Status\n");

        var service = CreateService();
        var result = await service.ValidateAsync(jobId, _actorId);

        result.Succeeded.Should().BeFalse();
        result.Errors.Values.SelectMany(v => v).Single().Should().Contain("no data rows");
        (await _context.ImportRecords.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Validate_RejectsAnUnknownColumnInsteadOfSilentlyDroppingIt()
    {
        var jobId = await StageAsync(
            "CaseNumber,Title,CrimeType,IncidentDate,Severity,Status,IpAddress\n"
            + "BX-7001,Synthetic,THF,2026-01-01,Low,Open,10.0.0.1\n");

        var service = CreateService();
        var result = await service.ValidateAsync(jobId, _actorId);

        result.Succeeded.Should().BeFalse();
        result.Errors.Values.SelectMany(v => v).Single().Should().Contain("does not import the column 'IpAddress'");
    }

    [Fact]
    public async Task Validate_RejectsAHeaderThatOmitsARequiredColumn()
    {
        var jobId = await StageAsync(
            "CaseNumber,Title,IncidentDate,Severity,Status\nBX-7002,Synthetic,2026-01-01,Low,Open\n");

        var service = CreateService();
        var result = await service.ValidateAsync(jobId, _actorId);

        result.Succeeded.Should().BeFalse();
        result.Errors.Values.SelectMany(v => v).Single().Should().Contain("CrimeType");
    }

    [Fact]
    public async Task Validate_RejectsARepeatedHeaderName()
    {
        var jobId = await StageAsync(
            "CaseNumber,CaseNumber,Title,CrimeType,IncidentDate,Severity,Status\n"
            + "BX-7003,BX-7003,Synthetic,THF,2026-01-01,Low,Open\n");

        var service = CreateService();
        var result = await service.ValidateAsync(jobId, _actorId);

        result.Succeeded.Should().BeFalse();
        result.Errors.Values.SelectMany(v => v).Single().Should().Contain("repeats the column");
    }

    [Fact]
    public async Task Validate_RejectsAMalformedQuotedValueAndMarksTheJobFailed()
    {
        var jobId = await StageAsync(Fixture("malformed.csv"));

        var service = CreateService();
        var result = await service.ValidateAsync(jobId, _actorId);

        result.Succeeded.Should().BeFalse();
        result.Errors.Values.SelectMany(v => v).Single().Should().Contain("ends inside a quoted value");

        var job = await _context.ImportJobs.SingleAsync(j => j.Id == jobId);
        job.Status.Should().Be(ImportStatus.Failed);
        (await _context.AuditLogs.AnyAsync(l =>
            l.Action == AuditActionNames.ImportFailed && l.EntityId == jobId)).Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ReportsOneProblemPerRejectedRowWithRowNumberAndColumn()
    {
        var jobId = await StageAsync(Fixture("invalid.csv"));

        var service = CreateService();
        var result = await service.ValidateAsync(jobId, _actorId);

        result.Succeeded.Should().BeTrue();
        var validation = result.Value!;

        validation.TotalRows.Should().Be(13);
        validation.ValidRows.Should().Be(0);
        validation.InvalidRows.Should().Be(13);

        validation.Issues.Should().OnlyContain(issue => issue.RowNumber > 0);
        validation.Issues.Select(i => i.Code).Should().Contain(
            $"CrimeType.{BulkImportIssueCodes.UnknownCrimeType}");
        validation.Issues.Should().Contain(i =>
            i.Code == $"IncidentDate.{BulkImportIssueCodes.InvalidDateFormat}" && i.RowNumber == 3);
        validation.Issues.Should().Contain(i =>
            i.Code == $"Status.{BulkImportIssueCodes.InvalidStatus}" && i.RowNumber == 5);
        validation.Issues.Should().Contain(i =>
            i.Code == $"Severity.{BulkImportIssueCodes.InvalidSeverity}" && i.RowNumber == 6);
        validation.Issues.Should().Contain(i =>
            i.Code == $"District.{BulkImportIssueCodes.UnknownDistrict}" && i.RowNumber == 7);
        validation.Issues.Should().Contain(i =>
            i.Code == $"PoliceStation.{BulkImportIssueCodes.StationDistrictMismatch}" && i.RowNumber == 8);
        validation.Issues.Should().Contain(i =>
            i.Code == $"Latitude.{BulkImportIssueCodes.InvalidCoordinates}" && i.RowNumber == 9);
        validation.Issues.Should().Contain(i =>
            i.Code == $"Longitude.{BulkImportIssueCodes.InvalidCoordinates}" && i.RowNumber == 10);
        validation.Issues.Should().Contain(i =>
            i.Code == $"Latitude.{BulkImportIssueCodes.CoordinatesIncomplete}" && i.RowNumber == 11);
        validation.Issues.Should().Contain(i =>
            i.Code == $"CaseNumber.{BulkImportIssueCodes.RequiredValueMissing}");
    }

    [Fact]
    public async Task Validate_RejectsAFutureIncidentDate()
    {
        var jobId = await StageAsync(Fixture("invalid.csv"));

        var service = CreateService();
        var result = await service.ValidateAsync(jobId, _actorId);

        result.Value!.Issues.Should().Contain(i =>
            i.Code == $"IncidentDate.{BulkImportIssueCodes.DateInFuture}" && i.RowNumber == 4);
    }

    [Fact]
    public async Task Validate_RejectsARetiredReferenceValueWithItsOwnMessage()
    {
        var jobId = await StageAsync(
            "CaseNumber,Title,CrimeType,IncidentDate,Severity,Status\n"
            + "BX-7010,Synthetic,PPK,2026-01-01,Low,Open\n");

        var service = CreateService();
        var result = await service.ValidateAsync(jobId, _actorId);

        result.Succeeded.Should().BeTrue();
        result.Value!.InvalidRows.Should().Be(1);
        result.Value.Issues.Single().Message.Should().Contain("retired");
    }

    [Fact]
    public async Task Validate_NamesFreeTextProblemsWithoutRepeatingTheContent()
    {
        var secretNarrative = new string('z', 2_100);

        var jobId = await StageAsync(
            "CaseNumber,Title,CrimeType,IncidentDate,Severity,Status,Description\n"
            + $"BX-7020,{secretNarrative},THF,2026-01-01,Low,Open,{new string('q', 2_100)}\n");

        var service = CreateService();
        var result = await service.ValidateAsync(jobId, _actorId);

        var issue = result.Value!.Issues.Single(i =>
            i.Code == $"Description.{BulkImportIssueCodes.ValueTooLong}");

        // The row is described, never quoted back: the supplied text is case narrative.
        issue.Message.Should().Contain("2,000").And.NotContain("qqq");
    }

    [Fact]
    public async Task Validate_WarnsAboutSceneDataWithNoDistrictButStillAcceptsTheRow()
    {
        var jobId = await StageAsync(
            "CaseNumber,Title,CrimeType,IncidentDate,Severity,Status,Address\n"
            + "BX-7030,Synthetic,THF,2026-01-01,Low,Open,1 Example Way\n");

        var service = CreateService();
        var result = await service.ValidateAsync(jobId, _actorId);

        result.Value!.ValidRows.Should().Be(1);
        result.Value.WarningRows.Should().Be(1);
        result.Value.Issues.Single().Severity.Should().Be(ImportIssueSeverity.Warning);
        result.Value.Issues.Single().Code.Should()
            .Be($"District.{BulkImportIssueCodes.LocationWithoutDistrict}");
    }

    [Fact]
    public async Task Validate_RejectsARowThatDoesNotLineUpWithTheHeader()
    {
        var jobId = await StageAsync(
            "CaseNumber,Title,CrimeType,IncidentDate,Severity,Status\n"
            + "BX-7040,Too few columns,THF\n");

        var service = CreateService();
        var result = await service.ValidateAsync(jobId, _actorId);

        result.Value!.InvalidRows.Should().Be(1);
        result.Value.Issues.Should().Contain(i =>
            i.Code == $"{BulkImportIssueCodes.RowField}.{BulkImportIssueCodes.MalformedRow}");
    }

    [Fact]
    public async Task Validate_StoresOneStagedRecordPerRowAndReusesItOnASecondPass()
    {
        var jobId = await StageAsync(Fixture("valid.csv"));
        var service = CreateService();

        await service.ValidateAsync(jobId, _actorId);
        var again = await service.ValidateAsync(jobId, _actorId);

        again.Succeeded.Should().BeTrue();
        again.Value!.TotalRows.Should().Be(3);
        (await _context.ImportRecords.CountAsync(r => r.ImportJobId == jobId)).Should().Be(3);
    }

    [Fact]
    public async Task Validate_StopsAtTheRowCeilingInsteadOfReadingAnUnboundedFile()
    {
        var builder = new StringBuilder("CaseNumber,Title,CrimeType,IncidentDate,Severity,Status\n");
        for (var i = 1; i <= BulkImportLimits.MaxDataRows + 2; i++)
        {
            builder.Append(
                $"BX-{i:D6},Synthetic row,THF,2026-01-01,Low,Open,CD,CHQ,,,\n");
        }

        // The upload ceiling is smaller than the row ceiling, so this file is only
        // reachable through a deliberately staged job: assert the parse itself refuses.
        var jobId = await StageAsync("CaseNumber,Title,CrimeType,IncidentDate,Severity,Status\nBX-1,x,THF,2026-01-01,Low,Open\n");

        var job = await _context.ImportJobs.SingleAsync(j => j.Id == jobId);
        var oversized = Encoding.UTF8.GetBytes(builder.ToString());
        File.WriteAllBytes(
            Path.Combine(_storageRoot, job.SourceFilePath), oversized);

        var service = CreateService();
        var result = await service.ValidateAsync(jobId, _actorId);

        result.Succeeded.Should().BeFalse();
        result.Errors.Values.SelectMany(v => v).Single().Should().Contain("data rows one import accepts");
    }

    [Fact]
    public async Task Validate_CapsStoredIssuesButStillCountsEveryOne()
    {
        var builder = new StringBuilder("CaseNumber,Title,CrimeType,IncidentDate,Severity,Status\n");
        for (var i = 1; i <= 40; i++)
        {
            // Every row is missing all six required columns, so 200+ issues exist.
            builder.Append($"BX-{i:D6},,,,,\n");
        }

        var jobId = await StageAsync(builder.ToString());

        var service = CreateService();
        var result = await service.ValidateAsync(jobId, _actorId);

        result.Succeeded.Should().BeTrue();
        result.Value!.Issues.Should().HaveCountLessOrEqualTo(BulkImportLimits.MaxShownIssues);
        result.Value.IssuesTruncated.Should().BeTrue();
        (await _context.ImportErrors.CountAsync(e => e.ImportJobId == jobId))
            .Should().BeLessOrEqualTo(BulkImportLimits.MaxStoredIssues);
    }

    [Fact]
    public async Task Validate_RefusesAJobThatAlreadyRan()
    {
        var jobId = await StageAsync(Fixture("valid.csv"));
        var service = CreateService();

        await service.ValidateAsync(jobId, _actorId);
        await service.ImportAsync(jobId, _actorId);

        var result = await service.ValidateAsync(jobId, _actorId);

        result.Succeeded.Should().BeFalse();
        result.Errors.Values.SelectMany(v => v).Single().Should().Contain("can no longer be validated");
    }

    // ---------------------------------------------------------------------- duplicates: §12

    [Fact]
    public async Task Validate_SeparatesInFileRepeatsFromCaseNumbersAlreadyRegistered()
    {
        var jobId = await StageAsync(Fixture("duplicate.csv"));

        var service = CreateService();
        var result = await service.ValidateAsync(jobId, _actorId);

        var validation = result.Value!;
        validation.TotalRows.Should().Be(5);
        validation.ValidRows.Should().Be(2);
        validation.DuplicateRows.Should().Be(3);
        validation.InvalidRows.Should().Be(0);

        validation.Issues.Should().ContainSingle(i =>
            i.Code == $"CaseNumber.{BulkImportIssueCodes.DuplicateInFile}" && i.RowNumber == 2);
        validation.Issues.Should().ContainSingle(i =>
            i.Code == $"CaseNumber.{BulkImportIssueCodes.DuplicateInFile}" && i.RowNumber == 3,
            "case numbers are compared without regard to case, like the existing unique index");
        validation.Issues.Should().ContainSingle(i =>
            i.Code == $"CaseNumber.{BulkImportIssueCodes.CaseNumberExists}" && i.RowNumber == 4);
    }

    // -------------------------------------------------------------------------- preview: §10

    [Fact]
    public async Task Preview_ShowsAcceptedRowsAndWritesNoCaseRecords()
    {
        var jobId = await StageAsync(Fixture("valid.csv"));
        var service = CreateService();

        var casesBefore = await _context.CrimeCases.CountAsync();
        var locationsBefore = await _context.Locations.CountAsync();

        await service.ValidateAsync(jobId, _actorId);
        var preview = await service.PreviewAsync(jobId, _actorId);

        preview.Succeeded.Should().BeTrue();
        preview.Value!.Rows.Should().HaveCount(3);
        preview.Value.ValidRows.Should().Be(3);
        preview.Value.Job.CanImport.Should().BeTrue();

        (await _context.CrimeCases.CountAsync()).Should().Be(casesBefore);
        (await _context.Locations.CountAsync()).Should().Be(locationsBefore);

        (await _context.AuditLogs.AnyAsync(l =>
            l.Action == AuditActionNames.ImportPreviewed && l.EntityId == jobId)).Should().BeTrue();
    }

    [Fact]
    public async Task Preview_OrdersCellsByTheDocumentedSchemaWhateverTheFileUsed()
    {
        var jobId = await StageAsync(
            "Title,Severity,CaseNumber,Status,CrimeType,IncidentDate\n"
            + "Synthetic reversed file,Low,BX-7050,Open,THF,2026-01-02\n");

        var service = CreateService();
        await service.ValidateAsync(jobId, _actorId);
        var preview = await service.PreviewAsync(jobId, _actorId);

        var cells = preview.Value!.Rows.Single().Cells;
        cells[0].Should().Be("BX-7050");
        cells[1].Should().Be("Synthetic reversed file");
        cells[2].Should().Be("THF");
        cells[3].Should().Be("2026-01-02");
    }

    [Fact]
    public async Task Preview_RequiresValidationFirst()
    {
        var jobId = await StageAsync(Fixture("valid.csv"));

        var service = CreateService();
        var result = await service.PreviewAsync(jobId, _actorId);

        result.Succeeded.Should().BeFalse();
        result.Errors.Values.SelectMany(v => v).Single().Should().Contain("Validate the file");
    }

    [Fact]
    public async Task Preview_CapsRowsShownWithoutHidingThatMoreWereAccepted()
    {
        var builder = new StringBuilder("CaseNumber,Title,CrimeType,IncidentDate,Severity,Status\n");
        for (var i = 1; i <= BulkImportLimits.MaxPreviewRows + 5; i++)
        {
            builder.Append($"BX-71{i:D3},Synthetic row,THF,2026-01-01,Low,Open\n");
        }

        var jobId = await StageAsync(builder.ToString());

        var service = CreateService();
        await service.ValidateAsync(jobId, _actorId);
        var preview = await service.PreviewAsync(jobId, _actorId);

        preview.Value!.Rows.Should().HaveCount(BulkImportLimits.MaxPreviewRows);
        preview.Value.ValidRows.Should().Be(BulkImportLimits.MaxPreviewRows + 5);
        preview.Value.MoreRowsBeyondPreview.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------- import: §11

    [Fact]
    public async Task Import_CreatesCasesLocationsAndLinksFromAcceptedRows()
    {
        var jobId = await StageAsync(Fixture("valid.csv"));

        var service = CreateService();
        await service.ValidateAsync(jobId, _actorId);
        var import = await service.ImportAsync(jobId, _actorId);

        import.Succeeded.Should().BeTrue(string.Join("; ", import.Errors.SelectMany(e => e.Value)));
        import.Value!.ImportedRows.Should().Be(3);

        var cases = await _context.CrimeCases.Where(c => c.CaseNumber.StartsWith("BX-900")).ToListAsync();
        cases.Should().HaveCount(3);

        var warehouse = cases.Single(c => c.CaseNumber == "BX-9001");
        warehouse.CrimeTypeId.Should().Be(_theftTypeId);
        warehouse.Severity.Should().Be(CaseSeverity.High);
        warehouse.Status.Should().Be(CaseStatus.Open);
        warehouse.IncidentDate.Should().Be(new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc));
        warehouse.PoliceStationId.Should().Be(_centralStationId);
        warehouse.CreatedById.Should().Be(_actorId);

        // Scene data reaches the register through the existing case-location shape.
        var link = await _context.CaseLocations
            .Include(l => l.Location)
            .SingleAsync(l => l.CrimeCaseId == warehouse.Id);
        link.LocationRole.Should().Be("Incident");
        link.Location.DistrictId.Should().Be(_centralDistrictId);
        link.Location.Latitude.Should().Be(-22.9);
        link.Location.Longitude.Should().Be(47.1);

        // A row with no district creates a case and no location: honest, not invented.
        var noScene = cases.Single(c => c.CaseNumber == "BX-9003");
        (await _context.CaseLocations.AnyAsync(l => l.CrimeCaseId == noScene.Id)).Should().BeFalse();

        (await _context.Investigations.CountAsync(i =>
            i.CrimeCaseId == warehouse.Id)).Should().Be(1);

        // The imported cases are ordinary register rows, so the rest of NETRA sees them.
        (await _context.ImportRecords
            .Where(r => r.ImportJobId == jobId && r.Status == ImportRecordStatus.Imported)
            .CountAsync()).Should().Be(3);

        var job = await _context.ImportJobs.SingleAsync(j => j.Id == jobId);
        job.Status.Should().Be(ImportStatus.Completed);
        job.SuccessfulRecords.Should().Be(3);
        job.CompletedAt.Should().Be(FixedNow);
    }

    [Fact]
    public async Task Import_SkipsDuplicatesAndEndsCompletedWithErrors()
    {
        var jobId = await StageAsync(Fixture("duplicate.csv"));

        var service = CreateService();
        await service.ValidateAsync(jobId, _actorId);
        var import = await service.ImportAsync(jobId, _actorId);

        import.Succeeded.Should().BeTrue();
        import.Value!.ImportedRows.Should().Be(2);
        import.Value.SkippedDuplicateRows.Should().Be(3);

        var job = await _context.ImportJobs.SingleAsync(j => j.Id == jobId);
        job.Status.Should().Be(ImportStatus.CompletedWithErrors);

        // Nothing was overwritten: the registered case is untouched.
        var existing = await _context.CrimeCases.SingleAsync(c => c.CaseNumber == "BX-9202");
        existing.Title.Should().Be("Case already registered before this import");
        (await _context.CrimeCases.CountAsync(c => c.CaseNumber == "BX-9202")).Should().Be(1);
    }

    [Fact]
    public async Task Import_RejectsAFileWithNothingAccepted()
    {
        var jobId = await StageAsync(Fixture("invalid.csv"));

        var service = CreateService();
        await service.ValidateAsync(jobId, _actorId);
        var import = await service.ImportAsync(jobId, _actorId);

        import.Succeeded.Should().BeFalse();
        import.Errors.Values.SelectMany(v => v).Single().Should().Contain("nothing to import");
        (await _context.CrimeCases.CountAsync(c => c.CaseNumber.StartsWith("BX-91"))).Should().Be(0);
    }

    [Fact]
    public async Task Import_RequiresValidationAndRefusesARawUpload()
    {
        var jobId = await StageAsync(Fixture("valid.csv"));

        var service = CreateService();
        var import = await service.ImportAsync(jobId, _actorId);

        import.Succeeded.Should().BeFalse();
        import.Errors.Values.SelectMany(v => v).Single().Should().Contain("Validate the file before importing");
        (await _context.CrimeCases.AnyAsync(c => c.CaseNumber == "BX-9001")).Should().BeFalse();
    }

    [Fact]
    public async Task Import_RefusesASecondSubmissionOfTheSameJob()
    {
        var jobId = await StageAsync(Fixture("valid.csv"));

        var service = CreateService();
        await service.ValidateAsync(jobId, _actorId);
        await service.ImportAsync(jobId, _actorId);

        var second = await service.ImportAsync(jobId, _actorId);

        second.Succeeded.Should().BeFalse();
        second.Errors.Values.SelectMany(v => v).Single().Should().Contain("cannot be run again");
        (await _context.CrimeCases.CountAsync(c => c.CaseNumber == "BX-9001")).Should().Be(1);
    }

    [Fact]
    public async Task Import_AbortsAndWritesNothingWhenACaseNumberAppearedSinceValidation()
    {
        var jobId = await StageAsync(Fixture("valid.csv"));

        var service = CreateService();
        await service.ValidateAsync(jobId, _actorId);

        // Another operator registers one of the numbers between validation and confirmation.
        _context.CrimeCases.Add(new CrimeCase
        {
            CaseNumber = "BX-9002",
            Title = "Registered while the import was waiting",
            CrimeTypeId = _theftTypeId,
            Status = CaseStatus.Open,
            Severity = CaseSeverity.Low,
            IncidentDate = FixedNow.AddDays(3),
            CreatedById = _actorId
        });
        await _context.SaveChangesAsync();

        var import = await service.ImportAsync(jobId, _actorId);

        import.Succeeded.Should().BeFalse();
        import.Errors.Values.SelectMany(v => v).Single().Should()
            .Contain("Nothing was imported and no cases were created");

        // The pre-commit re-check ran before anything was added to the change tracker, so
        // the aborted run leaves no partial batch behind.
        (await _context.CrimeCases.AnyAsync(c => c.CaseNumber == "BX-9001")).Should().BeFalse();
        (await _context.CrimeCases.AnyAsync(c => c.CaseNumber == "BX-9003")).Should().BeFalse();

        var job = await _context.ImportJobs.SingleAsync(j => j.Id == jobId);
        job.Status.Should().Be(ImportStatus.Failed);
        job.SuccessfulRecords.Should().Be(0);

        (await _context.AuditLogs.AnyAsync(l =>
            l.Action == AuditActionNames.ImportFailed && l.EntityId == jobId)).Should().BeTrue();
    }

    [Fact]
    public async Task Import_AbortsWhenTheStoredFileNoLongerMatchesItsValidatedResult()
    {
        var jobId = await StageAsync(Fixture("valid.csv"));

        var service = CreateService();
        await service.ValidateAsync(jobId, _actorId);

        var job = await _context.ImportJobs.SingleAsync(j => j.Id == jobId);
        File.WriteAllText(
            Path.Combine(_storageRoot, job.SourceFilePath),
            "CaseNumber,Title,CrimeType,IncidentDate,Severity,Status\n"
            + "BX-9001,Synthetic warehouse theft,THF,2026-01-12,High,Open\n"
            + "BX-9009,Added after validation,THF,2026-01-13,High,Open\n");

        var import = await service.ImportAsync(jobId, _actorId);

        import.Succeeded.Should().BeFalse();
        import.Errors.Values.SelectMany(v => v).Single().Should()
            .Contain("no longer matches its validated result");
        (await _context.CrimeCases.AnyAsync(c => c.CaseNumber == "BX-9009")).Should().BeFalse();
    }

    [Fact]
    public async Task Import_AuditsStartedAndCompletedWithoutFileContents()
    {
        var jobId = await StageAsync(Fixture("valid.csv"));

        var service = CreateService();
        await service.ValidateAsync(jobId, _actorId);
        await service.ImportAsync(jobId, _actorId);

        var actions = await _context.AuditLogs
            .Where(l => l.EntityId == jobId)
            .Select(l => l.Action)
            .ToListAsync();

        actions.Should().Contain([
            AuditActionNames.ImportUploaded,
            AuditActionNames.ImportValidated,
            AuditActionNames.ImportStarted,
            AuditActionNames.ImportCompleted
        ]);

        // The cases themselves are audited as created, so the register's own trail is complete.
        (await _context.AuditLogs.CountAsync(l =>
            l.Action == AuditActionNames.Created && l.EntityType == AuditEntityTypeNames.CrimeCase))
            .Should().Be(3);
    }

    // ------------------------------------------------------------------------ cancel: §10

    [Fact]
    public async Task Cancel_LeavesStagedRowsForAuditAndCreatesNoCases()
    {
        var jobId = await StageAsync(Fixture("valid.csv"));

        var service = CreateService();
        await service.ValidateAsync(jobId, _actorId);
        var cancelled = await service.CancelAsync(jobId, _actorId);

        cancelled.Succeeded.Should().BeTrue();
        cancelled.Value!.Status.Should().Be(ImportStatus.Cancelled);
        (await _context.ImportRecords.CountAsync(r => r.ImportJobId == jobId)).Should().Be(3);
        (await _context.CrimeCases.AnyAsync(c => c.CaseNumber == "BX-9001")).Should().BeFalse();

        var rerun = await service.ImportAsync(jobId, _actorId);
        rerun.Succeeded.Should().BeFalse();
    }

    // ---------------------------------------------------------------------- ownership: §19

    [Fact]
    public async Task Stages_RefuseAnOperatorWhoDidNotUploadTheFileAndAuditTheDenial()
    {
        var jobId = await StageAsync(Fixture("valid.csv"));
        var service = CreateService();
        var other = CreateServiceAs(_otherUserId);

        (await other.ValidateAsync(jobId, _otherUserId)).Status.Should().Be(OperationStatus.Forbidden);
        (await other.PreviewAsync(jobId, _otherUserId)).Status.Should().Be(OperationStatus.Forbidden);
        (await other.ImportAsync(jobId, _otherUserId)).Status.Should().Be(OperationStatus.Forbidden);
        (await other.CancelAsync(jobId, _otherUserId)).Status.Should().Be(OperationStatus.Forbidden);
        (await other.GetJobAsync(jobId, _otherUserId)).Status.Should().Be(OperationStatus.Forbidden);
        (await other.GetResultAsync(jobId, _otherUserId)).Status.Should().Be(OperationStatus.Forbidden);

        (await _context.CrimeCases.AnyAsync(c => c.CaseNumber == "BX-9001")).Should().BeFalse();

        var denials = await _context.AuditLogs
            .Where(l => l.Action == AuditActionNames.ImportAccessDenied && l.UserId == _otherUserId)
            .ToListAsync();
        denials.Should().HaveCount(6);
    }

    [Fact]
    public async Task Stages_ReportAMissingJobAsNotFound()
    {
        var service = CreateService();

        var result = await service.ValidateAsync(Guid.NewGuid(), _actorId);

        result.Status.Should().Be(OperationStatus.NotFound);
    }

    [Fact]
    public async Task RecentJobs_ListsOnlyTheOperatorsOwnJobsNewestFirst()
    {
        var first = await StageAsync(Fixture("valid.csv"), name: "First batch");

        // Jobs are stamped by the injected clock, so the second upload is genuinely newer.
        _instant = FixedInstant.AddMinutes(5);
        var second = await StageAsync(Fixture("duplicate.csv"), name: "Second batch");

        var service = CreateService();
        var mine = await service.GetRecentJobsAsync(_actorId);
        var theirs = await service.GetRecentJobsAsync(_otherUserId);

        mine.Select(j => j.Id).Should().BeEquivalentTo([first, second]);
        mine.Select(j => j.Id).Should().ContainInOrder([second, first]);
        mine[0].Name.Should().Be("Second batch");
        theirs.Should().BeEmpty();
    }

    [Fact]
    public async Task Requirements_DescribesTheContractAndTheConfiguredValues()
    {
        var service = CreateService();

        var requirements = await service.GetRequirementsAsync();

        requirements.Columns.Should().Equal(BulkImportSchema.Columns);
        BulkImportSchema.RequiredNames.Should().Equal(
            "CaseNumber", "Title", "CrimeType", "IncidentDate", "Severity", "Status");

        // Each offered value is labelled "CODE — Name", because reference columns accept
        // either spelling. Retired reference data must not be offered at all.
        requirements.CrimeTypes.Should().ContainSingle(type => type.StartsWith("THF", StringComparison.Ordinal));
        requirements.CrimeTypes.Should().NotContain(
            type => type.Contains("PPK", StringComparison.Ordinal)
                    || type.Contains("Pickpocketing", StringComparison.Ordinal),
            "retired values are not offered");
        requirements.Districts.Should().ContainSingle(district => district.StartsWith("CD", StringComparison.Ordinal));
        requirements.PoliceStations.Should()
            .ContainSingle(station => station.StartsWith("CHQ", StringComparison.Ordinal));
        requirements.MaxFileBytes.Should().Be(BulkImportLimits.MaxFileBytes);
    }

    // ------------------------------------------------------------------------- result page

    [Fact]
    public async Task GetResult_LinksCreatedCasesBackToTheirRows()
    {
        var jobId = await StageAsync(Fixture("valid.csv"));

        var service = CreateService();
        await service.ValidateAsync(jobId, _actorId);
        await service.ImportAsync(jobId, _actorId);

        var result = await service.GetResultAsync(jobId, _actorId);

        result.Succeeded.Should().BeTrue();
        result.Value!.ImportedCases.Should().HaveCount(3);
        result.Value.ImportedCases.Should().OnlyContain(c => c.CaseId != Guid.Empty);
        result.Value.ImportedCases.Select(c => c.CaseNumber)
            .Should().Contain(["BX-9001", "BX-9002", "BX-9003"]);
        result.Value.CompletedAt.Should().Be(FixedNow);
    }
}
