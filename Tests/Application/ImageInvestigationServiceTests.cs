using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NETRA.Application.Abstractions;
using NETRA.Application.ImageInvestigation;
using NETRA.Domain.Cases;
using NETRA.Domain.Enums;
using NETRA.Domain.Geography;
using NETRA.Domain.ImageInvestigation;
using NETRA.Domain.People;
using NETRA.Domain.Security;
using NETRA.Infrastructure.Data;
using NETRA.Infrastructure.Services;
using System.Text;
using Xunit;

namespace NETRA.Tests.Application;

/// <summary>
/// Verifies upload validation, secure storage, deterministic synthetic matching,
/// candidate resolution and audit recording for Image-Assisted Investigation
/// against EF Core's in-memory provider and a real FileStorageService rooted in
/// a temp directory. These are not live SQL Server tests.
/// </summary>
public sealed class ImageInvestigationServiceTests : IDisposable
{
    private readonly NetraDbContext _context;
    private readonly string _storageRoot;
    private readonly FileStorageService _storage;
    private readonly MockImageMatchProvider _provider;
    private readonly ImageInvestigationService _service;
    private readonly Guid _actorId;
    private readonly Guid _caseId;
    private readonly Guid _closedCaseId;
    private readonly Guid _personId;

    public ImageInvestigationServiceTests()
    {
        _context = new NetraDbContext(
            new DbContextOptionsBuilder<NetraDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        _storageRoot = Path.Combine(Path.GetTempPath(), $"netra-image-tests-{Guid.NewGuid():N}");
        _storage = new FileStorageService(
            Microsoft.Extensions.Options.Options.Create(new FileStorageOptions
            {
                RootPath = _storageRoot,
                MaxFileSizeBytes = 25 * 1024 * 1024
            }),
            AppContext.BaseDirectory);

        var actor = new ApplicationUser
        {
            UserName = "image.seed@netra.local",
            Email = "image.seed@netra.local",
            FirstName = "Image",
            LastName = "Seed",
            EmailConfirmed = true,
            IsActive = true
        };
        _actorId = actor.Id;

        var crimeType = new CrimeType { Name = "Theft", Code = "THF", IsActive = true };
        var crimeCase = new CrimeCase
        {
            CaseNumber = "IMG-001",
            Title = "Image investigation case",
            CrimeTypeId = crimeType.Id,
            Status = CaseStatus.Open,
            Severity = CaseSeverity.Medium,
            IncidentDate = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc),
            CreatedById = actor.Id
        };
        var closedCase = new CrimeCase
        {
            CaseNumber = "IMG-002",
            Title = "Closed image case",
            CrimeTypeId = crimeType.Id,
            Status = CaseStatus.Closed,
            Severity = CaseSeverity.Low,
            IncidentDate = new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc),
            CreatedById = actor.Id
        };
        var person = new Person
        {
            FirstName = "Dana",
            LastName = "Suspect",
            CreatedById = actor.Id,
            IsActive = true
        };
        var location = new Location { Name = "Warehouse", Type = "Scene", IsActive = true };

        _context.AddRange(actor, crimeType, crimeCase, closedCase, person, location);
        _context.SaveChanges();

        _caseId = crimeCase.Id;
        _closedCaseId = closedCase.Id;
        _personId = person.Id;

        _context.CasePersons.Add(new CasePerson
        {
            CrimeCaseId = crimeCase.Id,
            PersonId = person.Id,
            Role = PersonRole.Suspect
        });
        _context.CaseLocations.Add(new CaseLocation
        {
            CrimeCaseId = crimeCase.Id,
            LocationId = location.Id,
            LocationRole = "Incident"
        });
        _context.SaveChanges();

        var audit = new AuditService(
            _context,
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            new StubActorAccessor(_actorId));

        _provider = new MockImageMatchProvider(_context);
        _service = new ImageInvestigationService(_context, _storage, _provider, audit);
    }

    public void Dispose()
    {
        _context.Dispose();
        if (Directory.Exists(_storageRoot))
        {
            Directory.Delete(_storageRoot, recursive: true);
        }
    }

    private static byte[] PngBytes(string tag) =>
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }
            .Concat(Encoding.UTF8.GetBytes($"IHDR{tag}")).ToArray();

    private static byte[] JpegBytes(string tag) =>
        new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }
            .Concat(Encoding.UTF8.GetBytes($"JFIF{tag}-payload")).ToArray();

    private static byte[] WebpBytes(string tag) =>
        new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0x01, 0x00, 0x00, 0x00 }
            .Concat(Encoding.UTF8.GetBytes($"WEBP{tag}")).ToArray();

    private AnalyzeImageRequest Request(
        byte[] content,
        string fileName = "subject.png",
        string contentType = "image/png",
        Guid? caseId = null) => new()
        {
            CrimeCaseId = caseId ?? _caseId,
            OriginalFileName = fileName,
            ContentType = contentType,
            Length = content.Length,
            Content = new MemoryStream(content)
        };

    private async Task<OperationResult<ImageInvestigationResultDto>> UploadAsync(
        string tag, byte[]? content = null, string fileName = "subject.png") =>
        await _service.AnalyzeAsync(
            Request(content ?? PngBytes(tag), fileName), _actorId);

    private sealed class StubActorAccessor : IAuditActorAccessor
    {
        private readonly Guid _id;
        public StubActorAccessor(Guid id) => _id = id;
        public Guid GetUserId() => _id;
        public Guid GetUserId(System.Security.Claims.ClaimsPrincipal? principal) => _id;
    }

    private sealed class ThrowingProvider : IImageMatchProvider
    {
        public string Name => "Failing provider";

        public Task<ImageMatchResult> FindCandidatesAsync(
            ImageMatchQuery query, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Provider is down.");
    }

    // ---------- upload validation ----------

    [Fact]
    public async Task AnalyzeAsync_ValidPng_StoresRecordAndAuditsUploadAndAnalysis()
    {
        var result = await UploadAsync("alpha");

        result.Succeeded.Should().BeTrue();
        var record = await _context.ImageRecords.SingleAsync();
        record.ChecksumSha256.Should().HaveLength(64);
        record.FileSizeBytes.Should().BeGreaterThan(0);
        record.ContentType.Should().Be("image/png");
        record.FileExtension.Should().Be(".png");
        record.UploadedById.Should().Be(_actorId);
        record.CrimeCaseId.Should().Be(_caseId);

        var audits = await _context.AuditLogs
            .Where(entry => entry.EntityId == record.Id)
            .ToListAsync();
        audits.Should().Contain(entry =>
            entry.Action == AuditActionNames.EvidenceUploaded
            && entry.NewValues!.Contains("subject.png"));
        audits.Should().Contain(entry =>
            entry.Action == AuditActionNames.ImageAnalysisRequested
            && entry.NewValues!.Contains("Deterministic demo provider"));
        // The audit trail must never contain image bytes.
        audits.Should().OnlyContain(entry =>
            entry.NewValues!.Length <= 200 && !entry.NewValues.Contains("base64"));
        audits.Should().HaveCount(2);
    }

    [Fact]
    public async Task AnalyzeAsync_RejectsUnsupportedExtension_AndStoresNothing()
    {
        var result = await _service.AnalyzeAsync(
            Request(PngBytes("x"), "report.pdf", "application/pdf"), _actorId);

        result.Succeeded.Should().BeFalse();
        result.Status.Should().Be(OperationStatus.ValidationFailed);
        result.Errors["File"].Should().ContainSingle()
            .Which.Should().Contain("Only .jpg, .jpeg, .png, .webp images are accepted.");
        (await _context.ImageRecords.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AnalyzeAsync_RejectsMismatchedContentType()
    {
        var result = await _service.AnalyzeAsync(
            Request(PngBytes("x"), "subject.png", "text/plain"), _actorId);

        result.Errors["File"].Should().ContainSingle()
            .Which.Should().Be("The file content type must be image/jpeg, image/png or image/webp.");
    }

    [Fact]
    public async Task AnalyzeAsync_RejectsOversizedUpload()
    {
        var result = await _service.AnalyzeAsync(new AnalyzeImageRequest
        {
            CrimeCaseId = _caseId,
            OriginalFileName = "big.png",
            ContentType = "image/png",
            Length = ImageInvestigationLimits.MaxUploadBytes + 1,
            Content = new MemoryStream(PngBytes("small"))
        }, _actorId);

        result.Errors["File"].Should().ContainSingle()
            .Which.Should().Contain("maximum size of 10 MB");
    }

    [Theory]
    [InlineData("../evil.png")]
    [InlineData("dir/evil.png")]
    [InlineData(@"dir\evil.png")]
    public async Task AnalyzeAsync_RejectsPathTraversalFileNames(string fileName)
    {
        var result = await _service.AnalyzeAsync(Request(PngBytes("x"), fileName), _actorId);

        result.Errors["File"].Should().ContainSingle()
            .Which.Should().Be("The file name is not valid.");
        (await _context.ImageRecords.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AnalyzeAsync_RejectsMalformedImageBytesWithAcceptedExtension()
    {
        var result = await _service.AnalyzeAsync(
            Request(Encoding.UTF8.GetBytes("this is not an image at all"), "fake.png"), _actorId);

        result.Errors["File"].Should().ContainSingle()
            .Which.Should().Be("The file content does not match a supported image format.");
        (await _context.ImageRecords.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AnalyzeAsync_RejectsEmptyUpload()
    {
        var result = await _service.AnalyzeAsync(new AnalyzeImageRequest
        {
            CrimeCaseId = _caseId,
            OriginalFileName = "empty.png",
            ContentType = "image/png",
            Length = 0,
            Content = new MemoryStream(Array.Empty<byte>())
        }, _actorId);

        result.Errors["File"].Should().ContainSingle()
            .Which.Should().Be("Choose an image file to upload.");
    }

    [Fact]
    public async Task AnalyzeAsync_RejectsUnknownOrClosedCase()
    {
        var unknown = await _service.AnalyzeAsync(
            Request(PngBytes("x"), caseId: Guid.NewGuid()), _actorId);
        var closed = await _service.AnalyzeAsync(
            Request(PngBytes("y"), caseId: _closedCaseId), _actorId);

        unknown.Errors["CrimeCaseId"].Should().ContainSingle();
        closed.Errors["CrimeCaseId"].Should().ContainSingle()
            .Which.Should().Be("Select the case this investigation image belongs to.");
        (await _context.ImageRecords.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AnalyzeAsync_RejectsIdenticalImageAlreadyStored()
    {
        var bytes = PngBytes("dup");
        (await UploadAsync("dup", bytes)).Succeeded.Should().BeTrue();

        var second = await _service.AnalyzeAsync(Request(bytes), _actorId);

        second.Succeeded.Should().BeFalse();
        second.Errors["File"].Should().ContainSingle()
            .Which.Should().Be("An identical image is already stored in NETRA.");
        (await _context.ImageRecords.CountAsync()).Should().Be(1);
    }

    // ---------- secure storage ----------

    [Fact]
    public async Task AnalyzeAsync_UsesServerGeneratedStorageName_NotClientFileName()
    {
        var bytes = PngBytes("secure");
        var result = await UploadAsync("secure", bytes, "client name.png");

        var record = await _context.ImageRecords.SingleAsync();
        record.StoragePath.Should().StartWith("IMG-001/");
        record.StoragePath.Should().NotContain("client name");
        Path.GetFileName(record.StoragePath).Should().MatchRegex($"^[0-9a-f]{{32}}\\.png$");
        File.Exists(Path.Combine(_storageRoot, record.StoragePath)).Should().BeTrue();
        record.FileName.Should().Be("client name.png");
    }

    [Fact]
    public async Task OpenImageAsync_ReturnsStoredBytes_AndUnknownRecordIsNull()
    {
        var bytes = PngBytes("roundtrip");
        var result = await UploadAsync("roundtrip", bytes);

        var content = await _service.OpenImageAsync(result.Value!.ImageRecordId);
        using var memory = new MemoryStream();
        await content!.Content.CopyToAsync(memory);

        memory.ToArray().Should().Equal(bytes);
        content.ContentType.Should().Be("image/png");
        content.FileName.Should().Be("subject.png");

        (await _service.OpenImageAsync(Guid.NewGuid())).Should().BeNull();
    }

    // ---------- deterministic matching ----------

    [Fact]
    public async Task Provider_IsDeterministic_BoundedAndExcludesSource()
    {
        var first = SeedRecord(new string('a', 64));
        var second = SeedRecord(new string('b', 64));
        var third = SeedRecord(new string('c', 64));
        _context.ImageRecords.AddRange(first, second, third);
        await _context.SaveChangesAsync();

        var query = new ImageMatchQuery
        {
            SourceImageRecordId = first.Id,
            SourceChecksumSha256 = first.ChecksumSha256
        };

        var results = await _provider.FindCandidatesAsync(query);
        var repeat = await _provider.FindCandidatesAsync(query);

        repeat.Candidates.Should().BeEquivalentTo(results.Candidates);
        results.Candidates.Should().HaveCount(2);
        results.Candidates.Should().OnlyContain(candidate =>
            candidate.SimilarityScore >= 0.40m && candidate.SimilarityScore <= 0.95m);
        results.Candidates.Should().NotContain(candidate =>
            candidate.MatchedImageRecordId == first.Id);
        // Scores must actually vary across pairs: a flat score would hide an
        // arithmetic bug (e.g. integer division) in the deterministic mapping.
        results.Candidates.Select(candidate => candidate.SimilarityScore)
            .Distinct().Count().Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task AnalyzeAsync_FirstUpload_PersistsNoCandidatesInsteadOfFabricatingThem()
    {
        var result = await UploadAsync("lonely");

        result.Succeeded.Should().BeTrue();
        result.Value!.ProviderAvailable.Should().BeTrue();
        result.Value.Candidates.Should().BeEmpty();
        (await _context.ImageMatches.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AnalyzeAsync_OrdersCandidatesByScoreDescending_AndResolvesSuspectContext()
    {
        await UploadAsync("one");
        await UploadAsync("two");
        var third = await UploadAsync("three");

        var candidates = third.Value!.Candidates;
        candidates.Should().HaveCount(2);
        candidates.Select(candidate => candidate.SimilarityScore)
            .Should().BeInDescendingOrder();
        candidates.Should().OnlyContain(candidate =>
            candidate.Reference.StartsWith("DEMO-CAND-", StringComparison.Ordinal));
        candidates.Should().OnlyContain(candidate => candidate.DisplayName == "Dana Suspect");
        candidates.Should().OnlyContain(candidate => candidate.AssociatedCaseCount == 1);
        candidates.Should().OnlyContain(candidate => candidate.RelatedLocationCount == 1);
        candidates.Should().OnlyContain(candidate => candidate.SourceCaseNumber == "IMG-001");

        var stored = await _context.ImageMatches.CountAsync(match =>
            match.SourceImage.ChecksumSha256 == Sha("three"));
        stored.Should().Be(2);
        third.Value.ProviderName.Should().Be("Deterministic demo provider (synthetic)");
    }

    [Fact]
    public async Task AnalyzeAsync_CapsCandidatesAtResourceLimit()
    {
        for (var i = 0; i < 6; i++)
        {
            var result = await UploadAsync($"cap{i}");
            result.Succeeded.Should().BeTrue();
        }

        // The sixth upload compares against the five stored images but may only
        // return ImageInvestigationLimits.MaxCandidates of them.
        // (Each earlier upload also persisted matches; the new source's own set is bounded.)
        var sixth = await _context.ImageMatches.AsNoTracking()
            .Where(match => match.SourceImage.ChecksumSha256 == Sha("cap5"))
            .CountAsync();

        sixth.Should().Be(ImageInvestigationLimits.MaxCandidates);
    }

    [Fact]
    public async Task AnalyzeAsync_AcceptsJpegAndWebpSignatures()
    {
        (await _service.AnalyzeAsync(Request(JpegBytes("j1"), "one.jpg", "image/jpeg"), _actorId))
            .Succeeded.Should().BeTrue();
        (await _service.AnalyzeAsync(Request(WebpBytes("w1"), "two.webp", "image/webp"), _actorId))
            .Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task AnalyzeAsync_WhenProviderFails_StoresImageAndReportsUnavailableWithoutThrowing()
    {
        var audit = new AuditService(
            _context,
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            new StubActorAccessor(_actorId));
        var failing = new ImageInvestigationService(
            _context, _storage, new ThrowingProvider(), audit);

        var result = await failing.AnalyzeAsync(Request(PngBytes("boom")), _actorId);

        result.Succeeded.Should().BeTrue();
        result.Value!.ProviderAvailable.Should().BeFalse();
        result.Value.Candidates.Should().BeEmpty();
        (await _context.ImageRecords.CountAsync()).Should().Be(1);
    }

    // ---------- result viewing ----------

    [Fact]
    public async Task GetResultAsync_Unknown_ReturnsNull_Known_ReturnsPersistedMatchesAndAudits()
    {
        await UploadAsync("view1");
        await UploadAsync("view2");
        var recordId = (await _context.ImageRecords.FirstAsync(r => r.ChecksumSha256 == Sha("view2"))).Id;

        (await _service.GetResultAsync(Guid.NewGuid(), _actorId)).Should().BeNull();

        var view = await _service.GetResultAsync(recordId, _actorId);

        view.Should().NotBeNull();
        view!.Candidates.Should().HaveCount(1);
        view.CaseNumber.Should().Be("IMG-001");
        view.FileName.Should().Be("subject.png");
        (await _context.AuditLogs.AnyAsync(entry => entry.EntityId == recordId
            && entry.Action == AuditActionNames.ImageResultsViewed)).Should().BeTrue();
    }

    [Fact]
    public async Task GetResultAsync_ReorderedViewsStayDeterministic()
    {
        await UploadAsync("det1");
        var second = await UploadAsync("det2");
        var recordId = second.Value!.ImageRecordId;

        var firstView = await _service.GetResultAsync(recordId, _actorId);
        var secondView = await _service.GetResultAsync(recordId, _actorId);

        secondView!.Candidates.Should().BeEquivalentTo(firstView!.Candidates);
    }

    // ---------- match detail ----------

    [Fact]
    public async Task GetMatchDetailAsync_Unknown_ReturnsNull_Known_ReturnsContextAndAudits()
    {
        (await _service.GetMatchDetailAsync(Guid.NewGuid(), _actorId)).Should().BeNull();

        await UploadAsync("detail1");
        var second = await UploadAsync("detail2");
        var matchId = second.Value!.Candidates.Single().MatchId;

        var detail = await _service.GetMatchDetailAsync(matchId, _actorId);

        detail.Should().NotBeNull();
        detail!.DisplayName.Should().Be("Dana Suspect");
        detail.Reference.Should().StartWith("DEMO-CAND-");
        detail.SimilarityScore.Should().BeGreaterThanOrEqualTo(0.40m);
        detail.SimilarityScore.Should().BeLessThanOrEqualTo(0.95m);
        detail.AssociatedCases.Should().ContainSingle()
            .Which.CaseNumber.Should().Be("IMG-001");
        detail.RelatedLocations.Should().ContainSingle()
            .Which.Name.Should().Be("Warehouse");
        (await _context.AuditLogs.AnyAsync(entry => entry.EntityId == matchId
            && entry.Action == AuditActionNames.ImageMatchViewed)).Should().BeTrue();
    }

    // ---------- selectable cases ----------

    [Fact]
    public async Task GetSelectableCasesAsync_ExcludesClosedCases()
    {
        var cases = await _service.GetSelectableCasesAsync();

        cases.Should().ContainSingle();
        cases.Single().CaseNumber.Should().Be("IMG-001");
    }

    // ---------- helpers ----------

    /// <summary>Reproduces the service's checksum derivation for test lookups.</summary>
    private static string Sha(string tag)
    {
        var bytes = PngBytes(tag);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))
            .ToLowerInvariant();
    }

    private static ImageRecord SeedRecord(string checksum) => new()
    {
        CrimeCaseId = Guid.Empty,
        FileName = "seeded.png",
        FileExtension = ".png",
        ContentType = "image/png",
        FileSizeBytes = 24,
        StoragePath = $"IMG-001/seed-{checksum[..8]}.png",
        ChecksumSha256 = checksum,
        UploadedById = Guid.Empty
    };
}
