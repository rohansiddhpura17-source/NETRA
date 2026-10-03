using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NETRA.Application.Abstractions;
using NETRA.Application.CaseOperations;
using NETRA.Domain.Cases;
using NETRA.Domain.Enums;
using NETRA.Domain.Geography;
using NETRA.Domain.People;
using NETRA.Domain.Security;
using NETRA.Infrastructure.Data;
using NETRA.Infrastructure.Services;

namespace NETRA.Tests.Application;

/// <summary>
/// Service-level tests for <see cref="CaseService"/>. These run against the EF Core
/// in-memory provider; SQL Server behaviour is covered separately in the SQL Server notes.
/// </summary>
public sealed class CaseServiceTests : IDisposable
{
    private readonly NetraDbContext _context;
    private readonly CaseService _service;
    private Guid _actorId;
    private Guid _crimeTypeId;
    private Guid _districtId;
    private Guid _secondDistrictId;

    public CaseServiceTests()
    {
        _context = new NetraDbContext(
            new DbContextOptionsBuilder<NetraDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        var audit = new AuditService(
            _context,
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            new StubActorAccessor(_actorId));

        IFileStorageService storage = new InMemoryFileStorageService();

        _service = new CaseService(_context, audit, storage);
    }

    public void Dispose() => _context.Dispose();

    private sealed class StubActorAccessor : IAuditActorAccessor
    {
        private readonly Guid _id;
        public StubActorAccessor(Guid id) => _id = id;
        public Guid GetUserId() => _id;
        public Guid GetUserId(System.Security.Claims.ClaimsPrincipal? principal) => _id;
    }

    private sealed class InMemoryFileStorageService : IFileStorageService
    {
        public IReadOnlyCollection<string> AllowedExtensions { get; } =
            new[] { ".pdf", ".png", ".txt" };

        public long MaxFileSizeBytes => 1024 * 1024;

        public FileValidationResult Validate(string fileName, string contentType, long length)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return FileValidationResult.Invalid("A file name is required.");
            }

            var extension = Path.GetExtension(fileName).ToLowerInvariant();

            if (string.IsNullOrEmpty(extension))
            {
                return FileValidationResult.Invalid("The file must have an extension.");
            }

            if (!AllowedExtensions.Contains(extension))
            {
                return FileValidationResult.Invalid($"Files of type '{extension}' are not accepted.");
            }

            if (length <= 0)
            {
                return FileValidationResult.Invalid("The uploaded file is empty.");
            }

            if (length > MaxFileSizeBytes)
            {
                return FileValidationResult.Invalid("Too large.");
            }

            return FileValidationResult.Valid(extension);
        }

        public Task<StoredFileResult> SaveAsync(
            Stream content, string originalFileName, string contentType, string caseFolder,
            CancellationToken cancellationToken = default)
        {
            var validation = Validate(originalFileName, contentType, content.Length);
            if (!validation.IsValid)
            {
                throw new FileStorageException(validation.Error!);
            }

            return Task.FromResult(new StoredFileResult(
                $"{caseFolder}/stored{validation.Extension}",
                $"stored{validation.Extension}",
                "abc123",
                content.Length));
        }

        public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream?>(null);
    }

    private async Task SeedAsync(int caseCount = 5)
    {
        _actorId = Guid.NewGuid();

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
        _context.CrimeTypes.AddRange(theft, assault);
        await _context.SaveChangesAsync();
        _crimeTypeId = theft.Id;

        var central = new District { Name = "Central", Code = "CD", IsActive = true };
        var north = new District { Name = "North", Code = "ND", IsActive = true };
        _context.Districts.AddRange(central, north);
        await _context.SaveChangesAsync();
        _districtId = central.Id;
        _secondDistrictId = north.Id;

        var baseDate = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < caseCount; i++)
        {
            var district = i % 2 == 0 ? central : north;
            var incident = baseDate.AddDays(i);

            var crimeCase = new CrimeCase
            {
                CaseNumber = $"CC-2026-{i + 1:0000}",
                Title = $"Demo case {i + 1}",
                Description = $"Case number {i + 1} description",
                CrimeTypeId = i % 2 == 0 ? theft.Id : assault.Id,
                Status = i % 3 == 0 ? CaseStatus.Open : CaseStatus.Closed,
                Severity = i % 2 == 0 ? CaseSeverity.High : CaseSeverity.Low,
                IncidentDate = incident,
                CreatedById = _actorId,
                CreatedAt = incident
            };

            _context.CrimeCases.Add(crimeCase);

            var location = new Location
            {
                Name = $"Location {i + 1}",
                DistrictId = district.Id,
                CreatedAt = incident
            };
            _context.Locations.Add(location);

            _context.CaseLocations.Add(new CaseLocation
            {
                CrimeCaseId = crimeCase.Id,
                LocationId = location.Id,
                LocationRole = "Incident",
                AssociatedAt = incident
            });
        }

        await _context.SaveChangesAsync();
    }

    // ---------------------------------------------------------- filtering

    [Fact]
    public async Task SearchCasesAsync_WithoutFilters_ReturnsAllCases()
    {
        await SeedAsync();

        var result = await _service.SearchCasesAsync(new CaseFilterCriteria());

        result.Page.TotalCount.Should().Be(5);
        result.Page.Items.Should().HaveCount(5);
    }

    [Fact]
    public async Task SearchCasesAsync_FiltersBySearchTerm()
    {
        await SeedAsync();

        var result = await _service.SearchCasesAsync(new CaseFilterCriteria { Search = "Demo case 3" });

        result.Page.TotalCount.Should().Be(1);
        result.Page.Items[0].Title.Should().Be("Demo case 3");
    }

    [Fact]
    public async Task SearchCasesAsync_FiltersByStatus()
    {
        await SeedAsync();

        var result = await _service.SearchCasesAsync(
            new CaseFilterCriteria { Status = nameof(CaseStatus.Open) });

        result.Page.TotalCount.Should().Be(2);
        result.Page.Items.Should().OnlyContain(i => i.Status == nameof(CaseStatus.Open));
    }

    [Fact]
    public async Task SearchCasesAsync_FiltersBySeverity()
    {
        await SeedAsync();

        var result = await _service.SearchCasesAsync(
            new CaseFilterCriteria { Severity = nameof(CaseSeverity.High) });

        result.Page.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task SearchCasesAsync_FiltersByCrimeType()
    {
        await SeedAsync();

        var assaultType = await _context.CrimeTypes.FirstAsync(t => t.Code == "ASL");

        var result = await _service.SearchCasesAsync(
            new CaseFilterCriteria { CrimeTypeId = assaultType.Id });

        result.Page.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task SearchCasesAsync_FiltersByDistrictThroughLocationJoin()
    {
        await SeedAsync();

        var result = await _service.SearchCasesAsync(
            new CaseFilterCriteria { DistrictId = _districtId });

        result.Page.TotalCount.Should().Be(3);
        result.Page.Items.Should().OnlyContain(i => i.DistrictName == "Central");
    }

    [Fact]
    public async Task SearchCasesAsync_FiltersByDateRange()
    {
        await SeedAsync();

        var result = await _service.SearchCasesAsync(new CaseFilterCriteria
        {
            IncidentDateFrom = new DateTime(2026, 1, 2),
            IncidentDateTo = new DateTime(2026, 1, 3)
        });

        result.Page.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task SearchCasesAsync_CombinesFilters()
    {
        await SeedAsync();

        var result = await _service.SearchCasesAsync(new CaseFilterCriteria
        {
            Status = nameof(CaseStatus.Open),
            Severity = nameof(CaseSeverity.High),
            DistrictId = _districtId
        });

        // Open + High + Central => case 1 only (indices 0, 2 are High; 0, 3 are Open).
        result.Page.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task SearchCasesAsync_ReturnsNothingForUnknownSearchTerm()
    {
        await SeedAsync();

        var result = await _service.SearchCasesAsync(new CaseFilterCriteria { Search = "no-such-thing" });

        result.Page.TotalCount.Should().Be(0);
        result.Page.Items.Should().BeEmpty();
    }

    // --------------------------------------------------------- pagination

    [Fact]
    public async Task SearchCasesAsync_PaginatesResults()
    {
        await SeedAsync(7);

        var first = await _service.SearchCasesAsync(
            new CaseFilterCriteria { Page = 1, PageSize = 3 });

        first.Page.Items.Should().HaveCount(3);
        first.Page.TotalCount.Should().Be(7);
        first.Page.TotalPages.Should().Be(3);
        first.Page.From.Should().Be(1);
        first.Page.To.Should().Be(3);

        var last = await _service.SearchCasesAsync(
            new CaseFilterCriteria { Page = 3, PageSize = 3 });

        last.Page.Items.Should().HaveCount(1);
        last.Page.From.Should().Be(7);
        last.Page.To.Should().Be(7);
    }

    [Fact]
    public async Task SearchCasesAsync_ReturnsPageBeyondRangeAsEmpty()
    {
        await SeedAsync(2);

        var result = await _service.SearchCasesAsync(
            new CaseFilterCriteria { Page = 9, PageSize = 10 });

        result.Page.Items.Should().BeEmpty();
        result.Page.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task SearchCasesAsync_SortsOldestFirst()
    {
        await SeedAsync(3);

        var result = await _service.SearchCasesAsync(
            new CaseFilterCriteria { SortBy = CaseSortOptions.OldestFirst });

        result.Page.Items.Select(i => i.IncidentDate)
            .Should().BeInAscendingOrder();
    }

    // ----------------------------------------------------------- create

    [Fact]
    public async Task CreateCaseAsync_GeneratesCaseNumberServerSide()
    {
        await SeedAsync(0);

        var result = await _service.CreateCaseAsync(new CreateCaseRequest
        {
            Title = "Brand new case",
            CrimeTypeId = _crimeTypeId,
            Status = nameof(CaseStatus.Open),
            Severity = nameof(CaseSeverity.Medium),
            IncidentDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            DistrictId = _districtId
        }, _actorId);

        result.Succeeded.Should().BeTrue();

        var saved = await _context.CrimeCases.SingleAsync();
        saved.CaseNumber.Should().MatchRegex(@"^CC-\d{4}-0001$");
    }

    [Fact]
    public async Task CreateCaseAsync_UsesSuppliedUniqueCaseNumber()
    {
        await SeedAsync(0);

        var result = await _service.CreateCaseAsync(new CreateCaseRequest
        {
            CaseNumber = "CC-CUSTOM-9",
            Title = "Custom numbered case",
            CrimeTypeId = _crimeTypeId,
            Status = nameof(CaseStatus.Open),
            Severity = nameof(CaseSeverity.Low),
            IncidentDate = DateTime.UtcNow
        }, _actorId);

        result.Succeeded.Should().BeTrue();
        (await _context.CrimeCases.SingleAsync()).CaseNumber.Should().Be("CC-CUSTOM-9");
    }

    [Fact]
    public async Task CreateCaseAsync_RejectsDuplicateCaseNumberWithoutOverwriting()
    {
        await SeedAsync(1);
        var existingTitle = await _context.CrimeCases.Select(c => c.Title).FirstAsync();

        var result = await _service.CreateCaseAsync(new CreateCaseRequest
        {
            CaseNumber = "CC-2026-0001",
            Title = "Should not overwrite",
            CrimeTypeId = _crimeTypeId,
            Status = nameof(CaseStatus.Open),
            Severity = nameof(CaseSeverity.Low),
            IncidentDate = DateTime.UtcNow
        }, _actorId);

        result.Succeeded.Should().BeFalse();
        result.Status.Should().Be(OperationStatus.Conflict);
        result.Errors.Should().ContainKey("CaseNumber");

        // The original case is untouched.
        (await _context.CrimeCases.SingleAsync()).Title.Should().Be(existingTitle);
    }

    [Fact]
    public async Task CreateCaseAsync_RejectsMissingTitle()
    {
        await SeedAsync(0);

        var result = await _service.CreateCaseAsync(new CreateCaseRequest
        {
            CrimeTypeId = _crimeTypeId,
            Status = nameof(CaseStatus.Open),
            Severity = nameof(CaseSeverity.Low),
            IncidentDate = DateTime.UtcNow
        }, _actorId);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().ContainKey("Title");
    }

    [Fact]
    public async Task CreateCaseAsync_RejectsUnknownCrimeType()
    {
        await SeedAsync(0);

        var result = await _service.CreateCaseAsync(new CreateCaseRequest
        {
            Title = "Bad type",
            CrimeTypeId = Guid.NewGuid(),
            Status = nameof(CaseStatus.Open),
            Severity = nameof(CaseSeverity.Low),
            IncidentDate = DateTime.UtcNow
        }, _actorId);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().ContainKey("CrimeTypeId");
    }

    [Fact]
    public async Task CreateCaseAsync_RejectsInvalidStatusAndSeverity()
    {
        await SeedAsync(0);

        var result = await _service.CreateCaseAsync(new CreateCaseRequest
        {
            Title = "Bad enum",
            CrimeTypeId = _crimeTypeId,
            Status = "NotAStatus",
            Severity = "NotASeverity",
            IncidentDate = DateTime.UtcNow
        }, _actorId);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().ContainKey("Status");
        result.Errors.Should().ContainKey("Severity");
    }

    [Fact]
    public async Task CreateCaseAsync_RejectsFutureIncidentDate()
    {
        await SeedAsync(0);

        var result = await _service.CreateCaseAsync(new CreateCaseRequest
        {
            Title = "Future",
            CrimeTypeId = _crimeTypeId,
            Status = nameof(CaseStatus.Open),
            Severity = nameof(CaseSeverity.Low),
            IncidentDate = DateTime.UtcNow.AddDays(30)
        }, _actorId);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().ContainKey("IncidentDate");
    }

    [Theory]
    [InlineData(91.0, 0.0, "Latitude")]
    [InlineData(-91.0, 0.0, "Latitude")]
    [InlineData(0.0, 181.0, "Longitude")]
    [InlineData(0.0, -181.0, "Longitude")]
    public async Task CreateCaseAsync_RejectsOutOfRangeCoordinates(
        double latitude, double longitude, string expectedField)
    {
        await SeedAsync(0);

        var result = await _service.CreateCaseAsync(new CreateCaseRequest
        {
            Title = "Bad coordinates",
            CrimeTypeId = _crimeTypeId,
            Status = nameof(CaseStatus.Open),
            Severity = nameof(CaseSeverity.Low),
            IncidentDate = DateTime.UtcNow,
            DistrictId = _districtId,
            Latitude = latitude,
            Longitude = longitude
        }, _actorId);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().ContainKey(expectedField);
    }

    [Fact]
    public async Task CreateCaseAsync_CreatesSceneLocationLinkedToCase()
    {
        await SeedAsync(0);

        var result = await _service.CreateCaseAsync(new CreateCaseRequest
        {
            Title = "Scene case",
            CrimeTypeId = _crimeTypeId,
            Status = nameof(CaseStatus.Open),
            Severity = nameof(CaseSeverity.Low),
            IncidentDate = DateTime.UtcNow,
            DistrictId = _districtId,
            Address = "9 Example Road",
            Latitude = 51.5,
            Longitude = -0.1
        }, _actorId);

        result.Succeeded.Should().BeTrue();

        var link = await _context.CaseLocations.SingleAsync();
        link.LocationRole.Should().Be("Incident");
        link.Location!.DistrictId.Should().Be(_districtId);
        link.Location.Latitude.Should().Be(51.5);
    }

    [Fact]
    public async Task CreateCaseAsync_CreatesInvestigationWorkspace()
    {
        await SeedAsync(0);

        var result = await _service.CreateCaseAsync(new CreateCaseRequest
        {
            Title = "Investigation created",
            CrimeTypeId = _crimeTypeId,
            Status = nameof(CaseStatus.Open),
            Severity = nameof(CaseSeverity.Low),
            IncidentDate = DateTime.UtcNow
        }, _actorId);

        var investigation = await _context.Investigations.SingleAsync();
        investigation.CrimeCaseId.Should().Be(result.Value);
    }

    // ------------------------------------------------------------- edit

    [Fact]
    public async Task UpdateCaseAsync_AppliesChangesAndWritesAudit()
    {
        await SeedAsync(1);
        var caseId = await _context.CrimeCases.Select(c => c.Id).FirstAsync();

        var result = await _service.UpdateCaseAsync(caseId, new UpdateCaseRequest
        {
            Title = "Updated title",
            Description = "Updated description",
            CrimeTypeId = _crimeTypeId,
            Status = nameof(CaseStatus.Closed),
            Severity = nameof(CaseSeverity.Critical),
            IncidentDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
        }, _actorId);

        result.Succeeded.Should().BeTrue();

        var updated = await _context.CrimeCases.SingleAsync();
        updated.Title.Should().Be("Updated title");
        updated.Status.Should().Be(CaseStatus.Closed);
        updated.ClosedDate.Should().NotBeNull();

        var audit = await _context.AuditLogs
            .Where(a => a.EntityId == caseId)
            .ToListAsync();

        audit.Should().NotBeEmpty();
        audit.Should().Contain(a => a.PropertyName == nameof(CrimeCase.Title));
        audit.Should().Contain(a => a.PropertyName == nameof(CrimeCase.Status));
    }

    [Fact]
    public async Task UpdateCaseAsync_ReturnsNotFoundForUnknownCase()
    {
        await SeedAsync(1);

        var result = await _service.UpdateCaseAsync(Guid.NewGuid(), new UpdateCaseRequest
        {
            Title = "x",
            CrimeTypeId = _crimeTypeId,
            Status = nameof(CaseStatus.Open),
            Severity = nameof(CaseSeverity.Low),
            IncidentDate = DateTime.UtcNow
        }, _actorId);

        result.Status.Should().Be(OperationStatus.NotFound);
    }

    [Fact]
    public async Task UpdateCaseAsync_RejectsEmptyTitle()
    {
        await SeedAsync(1);
        var caseId = await _context.CrimeCases.Select(c => c.Id).FirstAsync();

        var result = await _service.UpdateCaseAsync(caseId, new UpdateCaseRequest
        {
            Title = "   ",
            CrimeTypeId = _crimeTypeId,
            Status = nameof(CaseStatus.Open),
            Severity = nameof(CaseSeverity.Low),
            IncidentDate = DateTime.UtcNow
        }, _actorId);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().ContainKey("Title");
    }

    [Fact]
    public async Task AssignInvestigatorAsync_UpdatesAssignmentAndAudits()
    {
        await SeedAsync(1);
        var caseId = await _context.CrimeCases.Select(c => c.Id).FirstAsync();

        var investigator = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "inv@netra.local",
            Email = "inv@netra.local",
            FirstName = "Inv",
            LastName = "Al",
            EmailConfirmed = true,
            IsActive = true
        };
        _context.Users.Add(investigator);
        await _context.SaveChangesAsync();

        var result = await _service.AssignInvestigatorAsync(
            caseId, new AssignInvestigatorRequest { InvestigatorId = investigator.Id }, _actorId);

        result.Succeeded.Should().BeTrue();
        (await _context.CrimeCases.SingleAsync()).AssignedInvestigatorId.Should().Be(investigator.Id);

        (await _context.AuditLogs.AnyAsync(a =>
            a.Action == AuditActionNames.InvestigatorAssigned)).Should().BeTrue();
    }

    [Fact]
    public async Task AssignInvestigatorAsync_RejectsUnknownInvestigator()
    {
        await SeedAsync(1);
        var caseId = await _context.CrimeCases.Select(c => c.Id).FirstAsync();

        var result = await _service.AssignInvestigatorAsync(
            caseId, new AssignInvestigatorRequest { InvestigatorId = Guid.NewGuid() }, _actorId);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().ContainKey("InvestigatorId");
    }

    // ------------------------------------------------ person association

    [Fact]
    public async Task AddPersonToCaseAsync_AssociatesExistingPerson()
    {
        await SeedAsync(1);
        var caseId = await _context.CrimeCases.Select(c => c.Id).FirstAsync();

        var person = new Person
        {
            FirstName = "Jo",
            LastName = "Smith",
            CreatedById = _actorId
        };
        _context.Persons.Add(person);
        await _context.SaveChangesAsync();

        var result = await _service.AddPersonToCaseAsync(caseId, new AddPersonToCaseRequest
        {
            PersonId = person.Id,
            Role = nameof(PersonRole.Witness),
            Notes = "Reported the incident."
        }, _actorId);

        result.Succeeded.Should().BeTrue();

        var link = await _context.CasePersons.SingleAsync();
        link.Role.Should().Be(PersonRole.Witness);
        link.Notes.Should().Be("Reported the incident.");

        (await _context.AuditLogs.AnyAsync(a =>
            a.Action == AuditActionNames.PersonAssociated)).Should().BeTrue();
    }

    [Fact]
    public async Task AddPersonToCaseAsync_RejectsUnknownPerson()
    {
        await SeedAsync(1);
        var caseId = await _context.CrimeCases.Select(c => c.Id).FirstAsync();

        var result = await _service.AddPersonToCaseAsync(caseId, new AddPersonToCaseRequest
        {
            PersonId = Guid.NewGuid(),
            Role = nameof(PersonRole.Suspect)
        }, _actorId);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().ContainKey("PersonId");
    }

    [Fact]
    public async Task AddPersonToCaseAsync_RejectsDuplicateAssociation()
    {
        await SeedAsync(1);
        var caseId = await _context.CrimeCases.Select(c => c.Id).FirstAsync();

        var person = new Person { FirstName = "Dup", LastName = "Person", CreatedById = _actorId };
        _context.Persons.Add(person);
        await _context.SaveChangesAsync();

        var request = new AddPersonToCaseRequest
        {
            PersonId = person.Id,
            Role = nameof(PersonRole.Witness)
        };

        (await _service.AddPersonToCaseAsync(caseId, request, _actorId)).Succeeded.Should().BeTrue();

        var second = await _service.AddPersonToCaseAsync(caseId, request, _actorId);
        second.Succeeded.Should().BeFalse();
        second.Status.Should().Be(OperationStatus.Conflict);
    }

    [Fact]
    public async Task AddPersonToCaseAsync_RejectsInvalidRole()
    {
        await SeedAsync(1);
        var caseId = await _context.CrimeCases.Select(c => c.Id).FirstAsync();

        var person = new Person { FirstName = "R", LastName = "P", CreatedById = _actorId };
        _context.Persons.Add(person);
        await _context.SaveChangesAsync();

        var result = await _service.AddPersonToCaseAsync(caseId, new AddPersonToCaseRequest
        {
            PersonId = person.Id,
            Role = "Chief"
        }, _actorId);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().ContainKey("Role");
    }

    [Fact]
    public async Task RemovePersonFromCaseAsync_RemovesLinkAndAudits()
    {
        await SeedAsync(1);
        var caseId = await _context.CrimeCases.Select(c => c.Id).FirstAsync();

        var person = new Person { FirstName = "R", LastName = "P", CreatedById = _actorId };
        _context.Persons.Add(person);
        await _context.SaveChangesAsync();

        var added = await _service.AddPersonToCaseAsync(caseId, new AddPersonToCaseRequest
        {
            PersonId = person.Id,
            Role = nameof(PersonRole.Witness)
        }, _actorId);

        var linkId = (await _context.CasePersons.Select(l => l.Id).FirstAsync());
        var result = await _service.RemovePersonFromCaseAsync(caseId, linkId, _actorId);

        result.Succeeded.Should().BeTrue();
        (await _context.CasePersons.CountAsync()).Should().Be(0);
        (await _context.AuditLogs.AnyAsync(a =>
            a.Action == AuditActionNames.PersonDisassociated)).Should().BeTrue();
        added.Succeeded.Should().BeTrue();
    }

    // ---------------------------------------------- location association

    [Fact]
    public async Task AddLocationToCaseAsync_UsesMappedLocationForeignKey()
    {
        await SeedAsync(1);
        var caseId = await _context.CrimeCases.Select(c => c.Id).FirstAsync();

        var location = new Location
        {
            Name = "Related place",
            DistrictId = _secondDistrictId
        };
        _context.Locations.Add(location);
        await _context.SaveChangesAsync();

        var result = await _service.AddLocationToCaseAsync(caseId, new AddLocationToCaseRequest
        {
            LocationId = location.Id,
            LocationRole = "Related",
            Notes = "Seen on CCTV."
        }, _actorId);

        result.Succeeded.Should().BeTrue();

        var link = await _context.CaseLocations.SingleAsync(l => l.LocationId == location.Id);
        link.CrimeCaseId.Should().Be(caseId);
        link.LocationRole.Should().Be("Related");
    }

    [Fact]
    public async Task AddLocationToCaseAsync_RejectsUnknownLocation()
    {
        await SeedAsync(1);
        var caseId = await _context.CrimeCases.Select(c => c.Id).FirstAsync();

        var result = await _service.AddLocationToCaseAsync(caseId, new AddLocationToCaseRequest
        {
            LocationId = Guid.NewGuid()
        }, _actorId);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().ContainKey("LocationId");
    }

    [Fact]
    public async Task AddLocationToCaseAsync_RejectsDuplicateLocation()
    {
        await SeedAsync(1);
        var caseId = await _context.CrimeCases.Select(c => c.Id).FirstAsync();
        var existingLocationId = await _context.CaseLocations
            .Select(cl => cl.LocationId).FirstAsync();

        var result = await _service.AddLocationToCaseAsync(caseId, new AddLocationToCaseRequest
        {
            LocationId = existingLocationId
        }, _actorId);

        result.Status.Should().Be(OperationStatus.Conflict);
    }

    // ------------------------------------------------------ investigation

    [Fact]
    public async Task AddEvidenceAsync_RecordsEvidenceAgainstCase()
    {
        await SeedAsync(1);
        var caseId = await _context.CrimeCases.Select(c => c.Id).FirstAsync();

        var result = await _service.AddEvidenceAsync(caseId, new AddEvidenceRequest
        {
            Title = "CCTV export",
            EvidenceType = "Digital",
            CollectedAt = DateTime.UtcNow,
            ChainOfCustody = "Sealed"
        }, _actorId);

        result.Succeeded.Should().BeTrue();

        var evidence = await _context.Evidence.SingleAsync();
        evidence.Title.Should().Be("CCTV export");
        evidence.EvidenceType.Should().Be(2);

        (await _context.AuditLogs.AnyAsync(a =>
            a.Action == AuditActionNames.EvidenceUploaded)).Should().BeTrue();
    }

    [Fact]
    public async Task AddEvidenceAsync_RejectsEmptyTitle()
    {
        await SeedAsync(1);
        var caseId = await _context.CrimeCases.Select(c => c.Id).FirstAsync();

        var result = await _service.AddEvidenceAsync(caseId, new AddEvidenceRequest
        {
            Title = "  ",
            EvidenceType = "Physical"
        }, _actorId);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().ContainKey("Title");
    }

    [Fact]
    public async Task AttachEvidenceFileAsync_RejectsDisallowedExtension()
    {
        await SeedAsync(1);
        var caseId = await _context.CrimeCases.Select(c => c.Id).FirstAsync();

        var evidence = await _service.AddEvidenceAsync(caseId, new AddEvidenceRequest
        {
            Title = "Item",
            EvidenceType = "Physical"
        }, _actorId);

        using var content = new MemoryStream("payload"u8.ToArray());

        var result = await _service.AttachEvidenceFileAsync(
            caseId,
            evidence.Value,
            new AddEvidenceFileRequest
            {
                OriginalFileName = "malicious.exe",
                ContentType = "application/octet-stream",
                Length = content.Length,
                Content = content
            },
            _actorId);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().ContainKey("File");
        (await _context.EvidenceFiles.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AttachEvidenceFileAsync_StoresMetadataForAcceptedFile()
    {
        await SeedAsync(1);
        var caseId = await _context.CrimeCases.Select(c => c.Id).FirstAsync();

        var evidence = await _service.AddEvidenceAsync(caseId, new AddEvidenceRequest
        {
            Title = "Item",
            EvidenceType = "Physical"
        }, _actorId);

        using var content = new MemoryStream("report contents"u8.ToArray());

        var result = await _service.AttachEvidenceFileAsync(
            caseId,
            evidence.Value,
            new AddEvidenceFileRequest
            {
                OriginalFileName = "report.pdf",
                ContentType = "application/pdf",
                Length = content.Length,
                Content = content
            },
            _actorId);

        result.Succeeded.Should().BeTrue();

        var file = await _context.EvidenceFiles.SingleAsync();
        // Display name keeps the original name; storage path is server-generated.
        file.FileName.Should().Be("report.pdf");
        file.StoragePath.Should().NotBe("report.pdf");
        file.ChecksumSha256.Should().NotBeNullOrEmpty();
        file.FileSizeBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AddInvestigationNoteAsync_RecordsNoteAndAudit()
    {
        await SeedAsync(1);
        var caseId = await _context.CrimeCases.Select(c => c.Id).FirstAsync();

        var result = await _service.AddInvestigationNoteAsync(caseId, new AddInvestigationNoteRequest
        {
            Content = "Reviewed access logs.",
            NoteType = "Observation"
        }, _actorId);

        result.Succeeded.Should().BeTrue();
        (await _context.InvestigationNotes.CountAsync()).Should().Be(1);
        (await _context.AuditLogs.AnyAsync(a =>
            a.Action == AuditActionNames.NoteCreated)).Should().BeTrue();
    }

    [Fact]
    public async Task AddInvestigationNoteAsync_RejectsEmptyContent()
    {
        await SeedAsync(1);
        var caseId = await _context.CrimeCases.Select(c => c.Id).FirstAsync();

        var result = await _service.AddInvestigationNoteAsync(caseId, new AddInvestigationNoteRequest
        {
            Content = "   ",
            NoteType = "Observation"
        }, _actorId);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().ContainKey("Content");
    }

    [Fact]
    public async Task AddTimelineEventAsync_RecordsEvent()
    {
        await SeedAsync(1);
        var caseId = await _context.CrimeCases.Select(c => c.Id).FirstAsync();

        var result = await _service.AddTimelineEventAsync(caseId, new AddTimelineEventRequest
        {
            Title = "Warrant issued",
            EventType = "Warrant",
            EventDateTime = new DateTime(2026, 2, 2, 10, 0, 0, DateTimeKind.Utc),
            Description = "Arrest warrant approved."
        }, _actorId);

        result.Succeeded.Should().BeTrue();

        var timelineEvent = await _context.TimelineEvents.SingleAsync();
        timelineEvent.EventType.Should().Be(3);
        (await _context.AuditLogs.AnyAsync(a =>
            a.Action == AuditActionNames.TimelineEventCreated)).Should().BeTrue();
    }

    [Fact]
    public async Task AddTimelineEventAsync_RejectsMissingDateTime()
    {
        await SeedAsync(1);
        var caseId = await _context.CrimeCases.Select(c => c.Id).FirstAsync();

        var result = await _service.AddTimelineEventAsync(caseId, new AddTimelineEventRequest
        {
            Title = "No date",
            EventType = "Incident",
            EventDateTime = default
        }, _actorId);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().ContainKey("EventDateTime");
    }

    [Fact]
    public async Task GetCaseDetailsAsync_ReturnsTimelineInChronologicalOrder()
    {
        await SeedAsync(1);
        var caseId = await _context.CrimeCases.Select(c => c.Id).FirstAsync();

        await _service.AddTimelineEventAsync(caseId, new AddTimelineEventRequest
        {
            Title = "Later", EventType = "Court",
            EventDateTime = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc)
        }, _actorId);

        await _service.AddTimelineEventAsync(caseId, new AddTimelineEventRequest
        {
            Title = "Earlier", EventType = "Incident",
            EventDateTime = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
        }, _actorId);

        var detail = await _service.GetCaseDetailsAsync(caseId);

        detail.Should().NotBeNull();
        detail!.Timeline.Select(t => t.Title).Should().ContainInOrder("Earlier", "Later");
    }

    [Fact]
    public async Task EnsureInvestigationAsync_IsIdempotent()
    {
        await SeedAsync(1);
        var caseId = await _context.CrimeCases.Select(c => c.Id).FirstAsync();

        var first = await _service.EnsureInvestigationAsync(caseId, _actorId);
        var second = await _service.EnsureInvestigationAsync(caseId, _actorId);

        first.Succeeded.Should().BeTrue();
        second.Succeeded.Should().BeTrue();
        second.Value.Should().Be(first.Value);
        (await _context.Investigations.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task GetCaseByInvestigationIdAsync_ResolvesTheOwningCase()
    {
        await SeedAsync(1);
        var caseId = await _context.CrimeCases.Select(c => c.Id).FirstAsync();

        var investigation = await _service.EnsureInvestigationAsync(caseId, _actorId);

        var resolved = await _service.GetCaseByInvestigationIdAsync(investigation.Value);

        resolved.Should().NotBeNull();
        resolved!.Id.Should().Be(caseId);
    }

    [Fact]
    public async Task GetLookupsAsync_ReturnsSeededReferenceData()
    {
        await SeedAsync();

        var lookups = await _service.GetLookupsAsync();

        lookups.CrimeTypes.Should().HaveCount(2);
        lookups.Districts.Should().HaveCount(2);
        lookups.Investigators.Should().HaveCount(1);
    }
}