using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NETRA.Application.Abstractions;
using NETRA.Application.CaseOperations;
using NETRA.Domain.Cases;
using NETRA.Domain.Enums;
using NETRA.Domain.Geography;
using NETRA.Domain.Investigation;
using NETRA.Domain.People;
using NETRA.Domain.Security;
using NETRA.Infrastructure.Data;

namespace NETRA.Infrastructure.Services;

/// <summary>
/// EF Core implementation of <see cref="ICaseService"/>.
/// </summary>
/// <remarks>
/// Query strategy: every read is composed as an <see cref="IQueryable{T}"/> and pushed to
/// the database with <c>AsNoTracking</c>, then projected straight into a DTO. Entities are
/// never materialised and never traversed in a loop, so there is no N+1 access.
/// </remarks>
public sealed class CaseService : ICaseService
{
    /// <summary>Role value used on <see cref="CaseLocation.LocationRole"/> for the primary scene.</summary>
    public const string IncidentLocationRole = "Incident";

    private const int MaxPageSize = 100;

    private readonly NetraDbContext _context;
    private readonly IAuditService _audit;
    private readonly IFileStorageService _fileStorage;

    public CaseService(NetraDbContext context, IAuditService audit, IFileStorageService fileStorage)
    {
        _context = context;
        _audit = audit;
        _fileStorage = fileStorage;
    }

    // ---------------------------------------------------------------- reads

    public async Task<CaseListResultDto> SearchCasesAsync(
        CaseFilterCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, criteria.Page);
        var pageSize = Math.Clamp(criteria.PageSize, 1, MaxPageSize);

        IQueryable<CrimeCase> query = _context.CrimeCases.AsNoTracking();

        query = ApplyFilters(query, criteria);

        // Total count and page contents are two cheap aggregate queries, not a full table load.
        var totalCount = await query.CountAsync(cancellationToken);

        query = ApplySort(query, criteria.SortBy);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CaseListItemDto
            {
                Id = c.Id,
                CaseNumber = c.CaseNumber,
                Title = c.Title,
                CrimeTypeName = c.CrimeType.Name,
                IncidentDate = c.IncidentDate,
                LastUpdatedDate = c.UpdatedAt,
                DistrictName = c.CaseLocations
                    .Where(cl => cl.LocationRole == IncidentLocationRole)
                    .Select(cl => cl.Location!.District!.Name)
                    .FirstOrDefault(),
                Status = c.Status.ToString(),
                Severity = c.Severity.ToString(),
                InvestigatorName = c.AssignedInvestigator == null
                    ? null
                    : (c.AssignedInvestigator.FirstName + " " + c.AssignedInvestigator.LastName).Trim(),
                PersonCount = c.CasePersons.Count,
                EvidenceCount = c.Investigation == null ? 0 : c.Investigation.EvidenceItems.Count
            })
            .ToListAsync(cancellationToken);

        var normalisedCriteria = new CaseFilterCriteria
        {
            Search = criteria.Search,
            CrimeTypeId = criteria.CrimeTypeId,
            DistrictId = criteria.DistrictId,
            Status = criteria.Status,
            Severity = criteria.Severity,
            IncidentDateFrom = criteria.IncidentDateFrom,
            IncidentDateTo = criteria.IncidentDateTo,
            SortBy = criteria.SortBy,
            Page = page,
            PageSize = pageSize
        };

        return new CaseListResultDto
        {
            Page = new PagedResult<CaseListItemDto>(items, totalCount, page, pageSize),
            Criteria = normalisedCriteria
        };
    }

    private static IQueryable<CrimeCase> ApplyFilters(IQueryable<CrimeCase> query, CaseFilterCriteria criteria)
    {
        if (!string.IsNullOrWhiteSpace(criteria.Search))
        {
            var term = criteria.Search.Trim();

            // Translated to SQL LIKE; no client-side evaluation.
            query = query.Where(c =>
                EF.Functions.Like(c.CaseNumber, $"%{term}%")
                || EF.Functions.Like(c.Title, $"%{term}%")
                || (c.Description != null && EF.Functions.Like(c.Description, $"%{term}%")));
        }

        if (criteria.CrimeTypeId is { } crimeTypeId)
        {
            query = query.Where(c => c.CrimeTypeId == crimeTypeId);
        }

        if (criteria.DistrictId is { } districtId)
        {
            // District lives on Location, reached through the CaseLocation join.
            query = query.Where(c => c.CaseLocations
                .Any(cl => cl.Location!.DistrictId == districtId));
        }

        if (Enum.TryParse<CaseStatus>(criteria.Status, ignoreCase: true, out var status))
        {
            query = query.Where(c => c.Status == status);
        }

        if (Enum.TryParse<CaseSeverity>(criteria.Severity, ignoreCase: true, out var severity))
        {
            query = query.Where(c => c.Severity == severity);
        }

        if (criteria.IncidentDateFrom is { } from)
        {
            var fromUtc = from.Date;
            query = query.Where(c => c.IncidentDate >= fromUtc);
        }

        if (criteria.IncidentDateTo is { } to)
        {
            // Inclusive of the whole end day.
            var toExclusive = to.Date.AddDays(1);
            query = query.Where(c => c.IncidentDate < toExclusive);
        }

        return query;
    }

    // Every sort ends with a unique key: SQL Server does not guarantee the order of tied rows
    // across OFFSET/FETCH pages, so a page boundary could otherwise repeat or drop a case.
    private static IQueryable<CrimeCase> ApplySort(IQueryable<CrimeCase> query, string? sortBy) => sortBy switch
    {
        CaseSortOptions.OldestFirst => query.OrderBy(c => c.IncidentDate).ThenBy(c => c.CaseNumber),
        CaseSortOptions.CaseNumberAsc => query.OrderBy(c => c.CaseNumber),
        CaseSortOptions.SeverityDesc => query
            .OrderByDescending(c => c.Severity)
            .ThenByDescending(c => c.IncidentDate)
            .ThenBy(c => c.CaseNumber),
        _ => query.OrderByDescending(c => c.IncidentDate).ThenBy(c => c.CaseNumber)
    };

    public async Task<CaseDetailDto?> GetCaseDetailsAsync(Guid caseId, CancellationToken cancellationToken = default)
    {
        var shell = await _context.CrimeCases
            .AsNoTracking()
            .Where(c => c.Id == caseId)
            .Select(c => new
            {
                c.Id,
                c.CaseNumber,
                c.Title,
                c.Description,
                CrimeTypeName = c.CrimeType.Name,
                Status = c.Status.ToString(),
                Severity = c.Severity.ToString(),
                c.IncidentDate,
                c.ReportedDate,
                c.ClosedDate,
                c.CreatedAt,
                InvestigatorName = c.AssignedInvestigator == null
                    ? null
                    : (c.AssignedInvestigator.FirstName + " " + c.AssignedInvestigator.LastName).Trim(),
                InvestigatorBadge = c.AssignedInvestigator!.BadgeNumber,
                CreatedByName = (c.CreatedBy.FirstName + " " + c.CreatedBy.LastName).Trim(),
                ModusOperandi = c.Investigation!.Summary,
                InvestigationId = c.Investigation!.Id
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (shell is null)
        {
            return null;
        }

        // Collections are fetched as separate projections rather than one large Include graph,
        // which keeps each query flat and avoids cartesian explosion.
        var persons = await ProjectPersons(caseId, cancellationToken);
        var locations = await ProjectLocations(caseId, cancellationToken);
        var evidence = await ProjectEvidence(caseId, cancellationToken);
        var notes = await ProjectNotes(caseId, cancellationToken);
        var timeline = await ProjectTimeline(caseId, cancellationToken);

        return new CaseDetailDto
        {
            Id = shell.Id,
            CaseNumber = shell.CaseNumber,
            Title = shell.Title,
            Description = shell.Description,
            CrimeTypeName = shell.CrimeTypeName,
            Status = shell.Status,
            Severity = shell.Severity,
            IncidentDate = shell.IncidentDate,
            ReportedDate = shell.ReportedDate,
            ClosedDate = shell.ClosedDate,
            CreatedAt = shell.CreatedAt,
            DistrictName = locations.FirstOrDefault(l =>
                string.Equals(l.LocationRole, IncidentLocationRole, StringComparison.Ordinal))?.DistrictName,
            PoliceStationName = locations.FirstOrDefault(l =>
                string.Equals(l.LocationRole, IncidentLocationRole, StringComparison.Ordinal))?.PoliceStationName,
            InvestigatorName = shell.InvestigatorName,
            InvestigatorBadge = shell.InvestigatorBadge,
            CreatedByName = shell.CreatedByName,
            ModusOperandi = shell.ModusOperandi,
            InvestigationId = shell.InvestigationId,
            Persons = persons,
            Locations = locations,
            Evidence = evidence,
            Notes = notes,
            Timeline = timeline
        };
    }

        public async Task<CaseDetailDto?> GetCaseByInvestigationIdAsync(
        Guid investigationId,
        CancellationToken cancellationToken = default)
    {
        var caseId = await _context.Investigations
            .AsNoTracking()
            .Where(i => i.Id == investigationId)
            .Select(i => (Guid?)i.CrimeCaseId)
            .SingleOrDefaultAsync(cancellationToken);

        return caseId is null ? null : await GetCaseDetailsAsync(caseId.Value, cancellationToken);
    }

    private Task<List<CasePersonDto>> ProjectPersons(Guid caseId, CancellationToken cancellationToken) =>
        _context.CasePersons
            .AsNoTracking()
            .Where(cp => cp.CrimeCaseId == caseId)
            .OrderBy(cp => cp.Role)
            .ThenBy(cp => cp.Person.LastName)
            .Select(cp => new CasePersonDto
            {
                Id = cp.Id,
                PersonId = cp.PersonId,
                FullName = (cp.Person.FirstName + " " + cp.Person.LastName).Trim(),
                NationalIdNumber = cp.Person.NationalIdNumber,
                DateOfBirth = cp.Person.DateOfBirth == null
                    ? null
                    : cp.Person.DateOfBirth.Value.ToString("yyyy-MM-dd"),
                Role = cp.Role.ToString(),
                Notes = cp.Notes,
                AssociatedAt = cp.AssociatedAt
            })
            .ToListAsync(cancellationToken);

    private Task<List<CaseLocationDto>> ProjectLocations(Guid caseId, CancellationToken cancellationToken) =>
        _context.CaseLocations
            .AsNoTracking()
            .Where(cl => cl.CrimeCaseId == caseId)
            .OrderBy(cl => cl.LocationRole)
            .Select(cl => new CaseLocationDto
            {
                Id = cl.Id,
                LocationId = cl.LocationId,
                Name = cl.Location!.Name,
                Address = cl.Location.Address,
                DistrictName = cl.Location.District!.Name,
                PoliceStationName = cl.Location.PoliceStation!.Name,
                Latitude = cl.Location.Latitude,
                Longitude = cl.Location.Longitude,
                LocationRole = cl.LocationRole,
                Notes = cl.Notes
            })
            .ToListAsync(cancellationToken);

    private async Task<List<EvidenceDto>> ProjectEvidence(Guid caseId, CancellationToken cancellationToken)
    {
        var evidence = await _context.Evidence
            .AsNoTracking()
            .Where(e => e.Investigation!.CrimeCaseId == caseId)
            .OrderByDescending(e => e.CollectedAt)
            .Select(e => new EvidenceDto
            {
                Id = e.Id,
                Title = e.Title,
                Description = e.Description,
                EvidenceType = EvidenceTypeName(e.EvidenceType),
                Status = EvidenceStatusName(e.EvidenceStatus),
                CollectedAt = e.CollectedAt,
                CollectedByName = e.CollectedByUser == null
                    ? e.CollectedBy
                    : (e.CollectedByUser.FirstName + " " + e.CollectedByUser.LastName).Trim(),
                ChainOfCustody = e.ChainOfCustody
            })
            .ToListAsync(cancellationToken);

        if (evidence.Count == 0)
        {
            return evidence;
        }

        var evidenceIds = evidence.Select(e => e.Id).ToList();

        // Project the flat scalar set the provider can translate, then assemble the DTOs
        // in memory. A second query, but no N+1 and no Include graph.
        var files = await _context.EvidenceFiles
            .AsNoTracking()
            .Where(f => evidenceIds.Contains(f.EvidenceId))
            .OrderByDescending(f => f.UploadedAt)
            .Select(f => new
            {
                f.Id,
                f.EvidenceId,
                f.FileName,
                f.ContentType,
                f.FileSizeBytes,
                f.ChecksumSha256,
                f.UploadedAt,
                UploadedByName = (f.UploadedBy.FirstName + " " + f.UploadedBy.LastName).Trim()
            })
            .ToListAsync(cancellationToken);

        var byEvidence = files
            .GroupBy(f => f.EvidenceId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<EvidenceFileDto>)group
                    .Select(f => new EvidenceFileDto
                    {
                        Id = f.Id,
                        FileName = f.FileName,
                        ContentType = f.ContentType,
                        FileSizeBytes = f.FileSizeBytes,
                        ChecksumSha256 = f.ChecksumSha256,
                        UploadedAt = f.UploadedAt,
                        UploadedByName = f.UploadedByName
                    })
                    .ToList());

        foreach (var item in evidence)
        {
            item.Files = byEvidence.TryGetValue(item.Id, out var list) ? list : Array.Empty<EvidenceFileDto>();
        }

        return evidence;
    }

    private Task<List<InvestigationNoteDto>> ProjectNotes(Guid caseId, CancellationToken cancellationToken) =>
        _context.InvestigationNotes
            .AsNoTracking()
            .Where(n => n.Investigation!.CrimeCaseId == caseId)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new InvestigationNoteDto
            {
                Id = n.Id,
                Content = n.Content,
                NoteType = NoteTypeName(n.NoteType),
                CreatedAt = n.CreatedAt,
                AuthorName = (n.CreatedBy.FirstName + " " + n.CreatedBy.LastName).Trim(),
                IsPrivate = n.IsPrivate
            })
            .ToListAsync(cancellationToken);

    private Task<List<TimelineEventDto>> ProjectTimeline(Guid caseId, CancellationToken cancellationToken) =>
        _context.TimelineEvents
            .AsNoTracking()
            .Where(te => te.Investigation!.CrimeCaseId == caseId)
            .OrderBy(te => te.EventDateTime)
            .Select(te => new TimelineEventDto
            {
                Id = te.Id,
                Title = te.Title,
                Description = te.Description,
                EventDateTime = te.EventDateTime,
                EventType = TimelineEventTypeName(te.EventType),
                Location = te.Location,
                AuthorName = (te.CreatedBy.FirstName + " " + te.CreatedBy.LastName).Trim()
            })
            .ToListAsync(cancellationToken);

    public async Task<CaseLookupDto> GetLookupsAsync(CancellationToken cancellationToken = default)
    {
        var crimeTypes = await _context.CrimeTypes
            .AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.DisplayOrder)
            .Select(t => new LookupItem { Id = t.Id, Label = t.Name })
            .ToListAsync(cancellationToken);

        var districts = await _context.Districts
            .AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.Name)
            .Select(d => new LookupItem { Id = d.Id, Label = d.Name })
            .ToListAsync(cancellationToken);

        var investigators = await _context.Set<ApplicationUser>()
            .AsNoTracking()
            .Where(u => u.IsActive)
            .OrderBy(u => u.LastName)
            .Select(u => new LookupItem
            {
                Id = u.Id,
                Label = (u.FirstName + " " + u.LastName + " (" + u.BadgeNumber + ")").Trim()
            })
            .ToListAsync(cancellationToken);

        var persons = await _context.Persons
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.LastName)
            .Select(p => new LookupItem
            {
                Id = p.Id,
                Label = (p.FirstName + " " + p.LastName).Trim()
            })
            .ToListAsync(cancellationToken);

        var locations = await _context.Locations
            .AsNoTracking()
            .Where(l => l.IsActive)
            .OrderBy(l => l.Name)
            .Select(l => new LookupItem { Id = l.Id, Label = l.Name })
            .ToListAsync(cancellationToken);

        return new CaseLookupDto
        {
            CrimeTypes = crimeTypes,
            Districts = districts,
            Investigators = investigators,
            Persons = persons,
            Locations = locations
        };
    }

    public async Task<IReadOnlyList<CaseChangeDto>> GetCaseHistoryAsync(
        Guid caseId,
        CancellationToken cancellationToken = default)
    {
        var caseIds = await _context.CrimeCases
            .AsNoTracking()
            .Where(c => c.Id == caseId)
            .Select(c => new { c.Id, InvestigationId = c.Investigation!.Id })
            .SingleOrDefaultAsync(cancellationToken);

        if (caseIds is null)
        {
            return Array.Empty<CaseChangeDto>();
        }

        var personLinkIds = await _context.CasePersons
            .AsNoTracking()
            .Where(cp => cp.CrimeCaseId == caseId)
            .Select(cp => cp.Id)
            .ToListAsync(cancellationToken);

        var locationLinkIds = await _context.CaseLocations
            .AsNoTracking()
            .Where(cl => cl.CrimeCaseId == caseId)
            .Select(cl => cl.Id)
            .ToListAsync(cancellationToken);

        var evidenceIds = await _context.Evidence
            .AsNoTracking()
            .Where(e => e.InvestigationId == caseIds.InvestigationId)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);

        var noteIds = await _context.InvestigationNotes
            .AsNoTracking()
            .Where(n => n.InvestigationId == caseIds.InvestigationId)
            .Select(n => n.Id)
            .ToListAsync(cancellationToken);

        var timelineIds = await _context.TimelineEvents
            .AsNoTracking()
            .Where(t => t.InvestigationId == caseIds.InvestigationId)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        // A single audit read covering the case and everything hanging off it.
        var entries = await _context.AuditLogs
            .AsNoTracking()
            .Where(a =>
                (a.EntityType == AuditEntityTypeNames.CrimeCase && a.EntityId == caseId)
                || (a.EntityType == AuditEntityTypeNames.CasePerson && personLinkIds.Contains(a.EntityId))
                || (a.EntityType == AuditEntityTypeNames.CaseLocation && locationLinkIds.Contains(a.EntityId))
                || (a.EntityType == AuditEntityTypeNames.Evidence && evidenceIds.Contains(a.EntityId))
                || (a.EntityType == AuditEntityTypeNames.EvidenceFile && evidenceIds.Contains(a.EntityId))
                || (a.EntityType == AuditEntityTypeNames.InvestigationNote && noteIds.Contains(a.EntityId))
                || (a.EntityType == AuditEntityTypeNames.TimelineEvent && timelineIds.Contains(a.EntityId)))
            .OrderByDescending(a => a.Timestamp)
            .Select(a => new CaseChangeDto
            {
                Id = a.Id,
                Field = a.PropertyName ?? a.EntityType,
                OldValue = a.OldValues,
                NewValue = a.NewValues,
                Action = a.Action,
                Timestamp = a.Timestamp,
                UserName = (a.User.FirstName + " " + a.User.LastName).Trim()
            })
            .Take(200)
            .ToListAsync(cancellationToken);

        return entries;
    }

    // --------------------------------------------------------------- writes

    public async Task<OperationResult<Guid>> CreateCaseAsync(
        CreateCaseRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        void AddError(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list))
            {
                list = [];
                errors[field] = list;
            }

            list.Add(message);
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            AddError(nameof(request.Title), "A case title is required.");
        }

        var crimeTypeExists = await _context.CrimeTypes
            .AsNoTracking()
            .AnyAsync(t => t.Id == request.CrimeTypeId, cancellationToken);

        if (!crimeTypeExists)
        {
            AddError(nameof(request.CrimeTypeId), "Select a valid crime type.");
        }

        if (!Enum.TryParse<CaseStatus>(request.Status, ignoreCase: true, out var status))
        {
            AddError(nameof(request.Status), "Select a valid status.");
        }

        if (!Enum.TryParse<CaseSeverity>(request.Severity, ignoreCase: true, out var severity))
        {
            AddError(nameof(request.Severity), "Select a valid severity.");
        }

        if (request.IncidentDate == default)
        {
            AddError(nameof(request.IncidentDate), "An incident date is required.");
        }
        else if (request.IncidentDate > DateTime.UtcNow.AddDays(1))
        {
            AddError(nameof(request.IncidentDate), "The incident date cannot be in the future.");
        }

        if (request.Latitude is { } lat && (lat < -90 || lat > 90))
        {
            AddError(nameof(request.Latitude), "Latitude must be between -90 and 90.");
        }

        if (request.Longitude is { } lng && (lng < -180 || lng > 180))
        {
            AddError(nameof(request.Longitude), "Longitude must be between -180 and 180.");
        }

        if (request.DistrictId is { } districtId)
        {
            var districtExists = await _context.Districts
                .AsNoTracking()
                .AnyAsync(d => d.Id == districtId, cancellationToken);

            if (!districtExists)
            {
                AddError(nameof(request.DistrictId), "Select a valid district.");
            }
        }

        if (request.InvestigatorId is { } investigatorId)
        {
            var investigatorExists = await _context.Set<ApplicationUser>()
                .AsNoTracking()
                .AnyAsync(u => u.Id == investigatorId, cancellationToken);

            if (!investigatorExists)
            {
                AddError(nameof(request.InvestigatorId), "Select a valid investigator.");
            }
        }

        if (errors.Count > 0)
        {
            return OperationResult<Guid>.ValidationFailed(ToErrorDictionary(errors));
        }

        // Case numbers are generated on the server. An explicitly supplied number is
        // honoured but must be unique; uniqueness is also enforced by a unique index.
        var caseNumber = await ResolveCaseNumberAsync(request.CaseNumber, cancellationToken);

        if (caseNumber is null)
        {
            return OperationResult<Guid>.Conflict(
                nameof(request.CaseNumber),
                $"Case number '{request.CaseNumber!.Trim()}' already exists.");
        }

        var crimeCase = new CrimeCase
        {
            CaseNumber = caseNumber,
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            CrimeTypeId = request.CrimeTypeId,
            Status = status,
            Severity = severity,
            IncidentDate = request.IncidentDate,
            ReportedDate = DateTime.UtcNow,
            CreatedById = actorUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            AssignedInvestigatorId = request.InvestigatorId
        };

        _context.CrimeCases.Add(crimeCase);

        // Scene location is stored as a Location row linked through CaseLocation, which
        // keeps district data normalised and uses the mapped LocationId foreign key.
        if (request.DistrictId is { } newDistrictId)
        {
            var location = new Location
            {
                Name = string.IsNullOrWhiteSpace(request.Address)
                    ? "Incident location"
                    : request.Address.Trim(),
                Type = "Incident",
                Address = request.Address?.Trim(),
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                DistrictId = newDistrictId,
                CreatedAt = DateTime.UtcNow
            };

            _context.Locations.Add(location);

            var link = new CaseLocation
            {
                CrimeCase = crimeCase,
                Location = location,
                LocationRole = IncidentLocationRole,
                AssociatedAt = DateTime.UtcNow
            };

            _context.CaseLocations.Add(link);
        }

        var investigation = new Investigation
        {
            CrimeCase = crimeCase,
            Title = $"Investigation — {crimeCase.CaseNumber}",
            Summary = request.ModusOperandi?.Trim(),
            StartedAt = DateTime.UtcNow,
            LeadInvestigatorId = request.InvestigatorId ?? actorUserId,
            Status = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Investigations.Add(investigation);

        _audit.Record(
            AuditEntityTypeNames.CrimeCase,
            crimeCase.Id,
            AuditActionNames.Created,
            actorUserId,
            newValue: $"Case {caseNumber} registered");

        // One SaveChanges commits the case, its location, its investigation and the audit
        // entry together.
        await _context.SaveChangesAsync(cancellationToken);

        return OperationResult<Guid>.Success(crimeCase.Id);
    }

    /// <summary>
    /// Returns a unique case number, or null when a caller-supplied number already exists.
    /// </summary>
    private async Task<string?> ResolveCaseNumberAsync(string? requested, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var candidate = requested.Trim();

            if (candidate.Length > 50)
            {
                candidate = candidate[..50];
            }

            var exists = await _context.CrimeCases
                .AsNoTracking()
                .AnyAsync(c => c.CaseNumber == candidate, cancellationToken);

            return exists ? null : candidate;
        }

        // Deterministic sequential scheme: CC-yyyy-NNNN
        var year = DateTime.UtcNow.Year;
        var prefix = $"CC-{year}-";

        var highest = await _context.CrimeCases
            .AsNoTracking()
            .Where(c => c.CaseNumber.StartsWith(prefix))
            .OrderByDescending(c => c.CaseNumber)
            .Select(c => c.CaseNumber)
            .FirstOrDefaultAsync(cancellationToken);

        var next = 1;
        if (highest is not null
            && int.TryParse(highest[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            next = parsed + 1;
        }

        // Guard against a concurrent insert claiming the same number.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var candidate = $"{prefix}{next:0000}";

            var exists = await _context.CrimeCases
                .AsNoTracking()
                .AnyAsync(c => c.CaseNumber == candidate, cancellationToken);

            if (!exists)
            {
                return candidate;
            }

            next++;
        }

        // Fall back to a collision-resistant value rather than overwriting an existing case.
        return $"{prefix}{next:0000}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
    }

    public async Task<OperationResult> UpdateCaseAsync(
        Guid caseId,
        UpdateCaseRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var crimeCase = await _context.CrimeCases.FirstOrDefaultAsync(c => c.Id == caseId, cancellationToken);
        if (crimeCase is null)
        {
            return OperationResult.NotFound("Crime case not found.");
        }

        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            errors[nameof(request.Title)] = ["A case title is required."];
        }

        if (!Enum.TryParse<CaseStatus>(request.Status, ignoreCase: true, out var status))
        {
            errors[nameof(request.Status)] = ["Select a valid status."];
        }

        if (!Enum.TryParse<CaseSeverity>(request.Severity, ignoreCase: true, out var severity))
        {
            errors[nameof(request.Severity)] = ["Select a valid severity."];
        }

        if (request.IncidentDate == default)
        {
            errors[nameof(request.IncidentDate)] = ["An incident date is required."];
        }
        else if (request.IncidentDate > DateTime.UtcNow.AddDays(1))
        {
            errors[nameof(request.IncidentDate)] = ["The incident date cannot be in the future."];
        }

        var crimeTypeExists = await _context.CrimeTypes
            .AsNoTracking()
            .AnyAsync(t => t.Id == request.CrimeTypeId, cancellationToken);

        if (!crimeTypeExists)
        {
            errors[nameof(request.CrimeTypeId)] = ["Select a valid crime type."];
        }

        if (errors.Count > 0)
        {
            return OperationResult.ValidationFailed(ToErrorDictionary(errors));
        }

        // Field-level change tracking for the audit trail.
        RecordChange(crimeCase.Title, request.Title.Trim(), nameof(CrimeCase.Title), crimeCase.Id, actorUserId);
        RecordChange(crimeCase.Status.ToString(), status.ToString(), nameof(CrimeCase.Status), crimeCase.Id, actorUserId);
        RecordChange(crimeCase.Severity.ToString(), severity.ToString(), nameof(CrimeCase.Severity), crimeCase.Id, actorUserId);
        RecordChange(crimeCase.IncidentDate.ToString("O"), request.IncidentDate.ToString("O"), nameof(CrimeCase.IncidentDate), crimeCase.Id, actorUserId);
        RecordChange(crimeCase.CrimeTypeId.ToString(), request.CrimeTypeId.ToString(), nameof(CrimeCase.CrimeTypeId), crimeCase.Id, actorUserId);
        RecordChange(crimeCase.Description, request.Description?.Trim(), nameof(CrimeCase.Description), crimeCase.Id, actorUserId);

        crimeCase.Title = request.Title.Trim();
        crimeCase.Description = request.Description?.Trim();
        crimeCase.Status = status;
        crimeCase.Severity = severity;
        crimeCase.IncidentDate = request.IncidentDate;
        crimeCase.CrimeTypeId = request.CrimeTypeId;
        crimeCase.UpdatedAt = DateTime.UtcNow;

        if (status == CaseStatus.Closed && crimeCase.ClosedDate is null)
        {
            crimeCase.ClosedDate = DateTime.UtcNow;
        }

        var investigation = await _context.Investigations
            .FirstOrDefaultAsync(i => i.CrimeCaseId == caseId, cancellationToken);

        if (investigation is not null)
        {
            RecordChange(
                investigation.Summary,
                request.ModusOperandi?.Trim(),
                "ModusOperandi",
                crimeCase.Id,
                actorUserId);

            investigation.Summary = request.ModusOperandi?.Trim();
            investigation.UpdatedAt = DateTime.UtcNow;
        }

        _audit.Record(
            AuditEntityTypeNames.CrimeCase,
            crimeCase.Id,
            AuditActionNames.Edited,
            actorUserId,
            newValue: "Case details updated");

        await _context.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    private void RecordChange(string? oldValue, string? newValue, string field, Guid caseId, Guid actorUserId)
    {
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            return;
        }

        _audit.Record(
            AuditEntityTypeNames.CrimeCase,
            caseId,
            AuditActionNames.Edited,
            actorUserId,
            propertyName: field,
            oldValue: oldValue,
            newValue: newValue);
    }

    public async Task<OperationResult> AssignInvestigatorAsync(
        Guid caseId,
        AssignInvestigatorRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var crimeCase = await _context.CrimeCases.FirstOrDefaultAsync(c => c.Id == caseId, cancellationToken);
        if (crimeCase is null)
        {
            return OperationResult.NotFound("Crime case not found.");
        }

        if (request.InvestigatorId is { } investigatorId)
        {
            var exists = await _context.Set<ApplicationUser>()
                .AsNoTracking()
                .AnyAsync(u => u.Id == investigatorId, cancellationToken);

            if (!exists)
            {
                return OperationResult.ValidationFailed(new Dictionary<string, string[]>
                {
                    [nameof(request.InvestigatorId)] = ["Select a valid investigator."]
                });
            }
        }

        var previous = crimeCase.AssignedInvestigatorId;

        crimeCase.AssignedInvestigatorId = request.InvestigatorId;
        crimeCase.UpdatedAt = DateTime.UtcNow;

        var investigation = await _context.Investigations
            .FirstOrDefaultAsync(i => i.CrimeCaseId == caseId, cancellationToken);

        if (investigation is not null && request.InvestigatorId is { } leadId)
        {
            investigation.LeadInvestigatorId = leadId;
            investigation.UpdatedAt = DateTime.UtcNow;
        }

        _audit.Record(
            AuditEntityTypeNames.CrimeCase,
            crimeCase.Id,
            AuditActionNames.InvestigatorAssigned,
            actorUserId,
            propertyName: nameof(CrimeCase.AssignedInvestigatorId),
            oldValue: previous?.ToString(),
            newValue: request.InvestigatorId?.ToString());

        await _context.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    // ---------------------------------------------------- person association

    public async Task<OperationResult<Guid>> AddPersonToCaseAsync(
        Guid caseId,
        AddPersonToCaseRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var crimeCase = await _context.CrimeCases
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == caseId, cancellationToken);

        if (crimeCase is null)
        {
            return OperationResult<Guid>.NotFound("Crime case not found.");
        }

        // Persons are only ever associated, never implicitly created.
        var personExists = await _context.Persons
            .AsNoTracking()
            .AnyAsync(p => p.Id == request.PersonId, cancellationToken);

        if (!personExists)
        {
            return OperationResult<Guid>.ValidationFailed(new Dictionary<string, string[]>
            {
                [nameof(request.PersonId)] = ["Select an existing person."]
            });
        }

        if (!Enum.TryParse<PersonRole>(request.Role, ignoreCase: true, out var role))
        {
            return OperationResult<Guid>.ValidationFailed(new Dictionary<string, string[]>
            {
                [nameof(request.Role)] = ["Select a valid role."]
            });
        }

        var alreadyLinked = await _context.CasePersons
            .AnyAsync(cp => cp.CrimeCaseId == caseId && cp.PersonId == request.PersonId, cancellationToken);

        if (alreadyLinked)
        {
            return OperationResult<Guid>.Conflict(
                nameof(request.PersonId),
                "That person is already associated with this case.");
        }

        var link = new CasePerson
        {
            CrimeCaseId = caseId,
            PersonId = request.PersonId,
            Role = role,
            Notes = request.Notes?.Trim(),
            AssociatedAt = DateTime.UtcNow,
            AssociatedById = actorUserId
        };

        _context.CasePersons.Add(link);

        _audit.Record(
            AuditEntityTypeNames.CasePerson,
            link.Id,
            AuditActionNames.PersonAssociated,
            actorUserId,
            propertyName: nameof(CasePerson.Role),
            newValue: role.ToString());

        await _context.SaveChangesAsync(cancellationToken);

        return OperationResult<Guid>.Success(link.Id);
    }

    public async Task<OperationResult> RemovePersonFromCaseAsync(
        Guid caseId,
        Guid casePersonId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var link = await _context.CasePersons
            .FirstOrDefaultAsync(cp => cp.Id == casePersonId && cp.CrimeCaseId == caseId, cancellationToken);

        if (link is null)
        {
            return OperationResult.NotFound("Association not found.");
        }

        var role = link.Role.ToString();
        var personId = link.PersonId;

        _context.CasePersons.Remove(link);

        _audit.Record(
            AuditEntityTypeNames.CasePerson,
            casePersonId,
            AuditActionNames.PersonDisassociated,
            actorUserId,
            propertyName: nameof(CasePerson.PersonId),
            oldValue: $"{personId} ({role})");

        await _context.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    // -------------------------------------------------- location association

    public async Task<OperationResult<Guid>> AddLocationToCaseAsync(
        Guid caseId,
        AddLocationToCaseRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var caseExists = await _context.CrimeCases
            .AsNoTracking()
            .AnyAsync(c => c.Id == caseId, cancellationToken);

        if (!caseExists)
        {
            return OperationResult<Guid>.NotFound("Crime case not found.");
        }

        var locationExists = await _context.Locations
            .AsNoTracking()
            .AnyAsync(l => l.Id == request.LocationId, cancellationToken);

        if (!locationExists)
        {
            return OperationResult<Guid>.ValidationFailed(new Dictionary<string, string[]>
            {
                [nameof(request.LocationId)] = ["Select an existing location."]
            });
        }

        var alreadyLinked = await _context.CaseLocations
            .AnyAsync(cl => cl.CrimeCaseId == caseId && cl.LocationId == request.LocationId, cancellationToken);

        if (alreadyLinked)
        {
            return OperationResult<Guid>.Conflict(
                nameof(request.LocationId),
                "That location is already associated with this case.");
        }

        // CaseLocation.LocationId is the mapped foreign key; no shadow property is involved.
        var link = new CaseLocation
        {
            CrimeCaseId = caseId,
            LocationId = request.LocationId,
            LocationRole = string.IsNullOrWhiteSpace(request.LocationRole)
                ? "Related"
                : request.LocationRole.Trim(),
            Notes = request.Notes?.Trim(),
            AssociatedAt = DateTime.UtcNow
        };

        _context.CaseLocations.Add(link);

        _audit.Record(
            AuditEntityTypeNames.CaseLocation,
            link.Id,
            AuditActionNames.LocationAssociated,
            actorUserId,
            propertyName: nameof(CaseLocation.LocationId),
            newValue: request.LocationId.ToString());

        await _context.SaveChangesAsync(cancellationToken);

        return OperationResult<Guid>.Success(link.Id);
    }

    public async Task<OperationResult> RemoveLocationFromCaseAsync(
        Guid caseId,
        Guid caseLocationId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var link = await _context.CaseLocations
            .FirstOrDefaultAsync(cl => cl.Id == caseLocationId && cl.CrimeCaseId == caseId, cancellationToken);

        if (link is null)
        {
            return OperationResult.NotFound("Association not found.");
        }

        var locationId = link.LocationId;

        _context.CaseLocations.Remove(link);

        _audit.Record(
            AuditEntityTypeNames.CaseLocation,
            caseLocationId,
            AuditActionNames.LocationDisassociated,
            actorUserId,
            propertyName: nameof(CaseLocation.LocationId),
            oldValue: locationId.ToString());

        await _context.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    // ------------------------------------------------------------- evidence

    public async Task<OperationResult<Guid>> AddEvidenceAsync(
        Guid caseId,
        AddEvidenceRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var investigationResult = await EnsureInvestigationAsync(caseId, actorUserId, cancellationToken);
        if (!investigationResult.Succeeded || investigationResult.Value == Guid.Empty)
        {
            return OperationResult<Guid>.NotFound("Investigation not found for this case.");
        }

        var investigationId = investigationResult.Value;

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return OperationResult<Guid>.ValidationFailed(new Dictionary<string, string[]>
            {
                [nameof(request.Title)] = ["An evidence name is required."]
            });
        }

        var evidence = new Evidence
        {
            InvestigationId = investigationId,
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            EvidenceType = ResolveEvidenceType(request.EvidenceType),
            EvidenceStatus = 1,
            CollectedAt = request.CollectedAt ?? DateTime.UtcNow,
            CollectionLocation = request.CollectionLocation?.Trim(),
            ChainOfCustody = request.ChainOfCustody?.Trim(),
            CollectedByUserId = actorUserId,
            CreatedAt = DateTime.UtcNow
        };

        _context.Evidence.Add(evidence);

        _audit.Record(
            AuditEntityTypeNames.Evidence,
            evidence.Id,
            AuditActionNames.EvidenceUploaded,
            actorUserId,
            newValue: evidence.Title);

        await _context.SaveChangesAsync(cancellationToken);

        return OperationResult<Guid>.Success(evidence.Id);
    }

    public async Task<OperationResult<Guid>> AttachEvidenceFileAsync(
        Guid caseId,
        Guid evidenceId,
        AddEvidenceFileRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var evidence = await _context.Evidence
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == evidenceId && e.Investigation!.CrimeCaseId == caseId, cancellationToken);

        if (evidence is null)
        {
            return OperationResult<Guid>.NotFound("Evidence not found for this case.");
        }

        var validation = _fileStorage.Validate(request.OriginalFileName, request.ContentType, request.Length);
        if (!validation.IsValid)
        {
            return OperationResult<Guid>.ValidationFailed(new Dictionary<string, string[]>
            {
                ["File"] = [validation.Error!]
            });
        }

        StoredFileResult stored;
        try
        {
            var caseFolder = await ResolveCaseFolderAsync(caseId, cancellationToken);
            stored = await _fileStorage.SaveAsync(
                request.Content,
                request.OriginalFileName,
                request.ContentType,
                caseFolder,
                cancellationToken);
        }
        catch (FileStorageException ex)
        {
            return OperationResult<Guid>.ValidationFailed(new Dictionary<string, string[]>
            {
                ["File"] = [ex.Message]
            });
        }

        var file = new EvidenceFile
        {
            EvidenceId = evidenceId,
            // The client name is retained only as a display label.
            FileName = Path.GetFileName(request.OriginalFileName),
            FileExtension = validation.Extension,
            ContentType = request.ContentType,
            FileSizeBytes = stored.SizeBytes,
            StoragePath = stored.StoragePath,
            ChecksumSha256 = stored.ChecksumSha256,
            UploadedAt = DateTime.UtcNow,
            UploadedById = actorUserId
        };

        _context.EvidenceFiles.Add(file);

        _audit.Record(
            AuditEntityTypeNames.EvidenceFile,
            file.Id,
            AuditActionNames.EvidenceUploaded,
            actorUserId,
            newValue: $"{file.FileName} ({stored.SizeBytes} bytes)");

        await _context.SaveChangesAsync(cancellationToken);

        return OperationResult<Guid>.Success(file.Id);
    }

    private async Task<string> ResolveCaseFolderAsync(Guid caseId, CancellationToken cancellationToken)
    {
        var caseNumber = await _context.CrimeCases
            .AsNoTracking()
            .Where(c => c.Id == caseId)
            .Select(c => c.CaseNumber)
            .SingleOrDefaultAsync(cancellationToken);

        return caseNumber ?? caseId.ToString("N");
    }

    // ------------------------------------------------- notes and timeline

    public async Task<OperationResult<Guid>> AddInvestigationNoteAsync(
        Guid caseId,
        AddInvestigationNoteRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var investigationResult = await EnsureInvestigationAsync(caseId, actorUserId, cancellationToken);
        if (!investigationResult.Succeeded || investigationResult.Value == Guid.Empty)
        {
            return OperationResult<Guid>.NotFound("Investigation not found for this case.");
        }

        var investigationId = investigationResult.Value;

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return OperationResult<Guid>.ValidationFailed(new Dictionary<string, string[]>
            {
                [nameof(request.Content)] = ["Note content is required."]
            });
        }

        var note = new InvestigationNote
        {
            InvestigationId = investigationId,
            Content = request.Content.Trim(),
            NoteType = ResolveNoteType(request.NoteType),
            IsPrivate = request.IsPrivate,
            CreatedAt = DateTime.UtcNow,
            CreatedById = actorUserId
        };

        _context.InvestigationNotes.Add(note);

        _audit.Record(
            AuditEntityTypeNames.InvestigationNote,
            note.Id,
            AuditActionNames.NoteCreated,
            actorUserId,
            newValue: note.NoteType.ToString());

        await _context.SaveChangesAsync(cancellationToken);

        return OperationResult<Guid>.Success(note.Id);
    }

    public async Task<OperationResult<Guid>> AddTimelineEventAsync(
        Guid caseId,
        AddTimelineEventRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var investigationResult = await EnsureInvestigationAsync(caseId, actorUserId, cancellationToken);
        if (!investigationResult.Succeeded || investigationResult.Value == Guid.Empty)
        {
            return OperationResult<Guid>.NotFound("Investigation not found for this case.");
        }

        var investigationId = investigationResult.Value;

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return OperationResult<Guid>.ValidationFailed(new Dictionary<string, string[]>
            {
                [nameof(request.Title)] = ["An event title is required."]
            });
        }

        if (request.EventDateTime == default)
        {
            return OperationResult<Guid>.ValidationFailed(new Dictionary<string, string[]>
            {
                [nameof(request.EventDateTime)] = ["An event date and time is required."]
            });
        }

        var timelineEvent = new TimelineEvent
        {
            InvestigationId = investigationId,
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            EventDateTime = request.EventDateTime,
            EventType = ResolveTimelineEventType(request.EventType),
            Location = request.Location?.Trim(),
            CreatedAt = DateTime.UtcNow,
            CreatedById = actorUserId
        };

        _context.TimelineEvents.Add(timelineEvent);

        _audit.Record(
            AuditEntityTypeNames.TimelineEvent,
            timelineEvent.Id,
            AuditActionNames.TimelineEventCreated,
            actorUserId,
            newValue: timelineEvent.Title);

        await _context.SaveChangesAsync(cancellationToken);

        return OperationResult<Guid>.Success(timelineEvent.Id);
    }

    public async Task<OperationResult<Guid>> EnsureInvestigationAsync(
        Guid caseId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var caseExists = await _context.CrimeCases
            .AsNoTracking()
            .AnyAsync(c => c.Id == caseId, cancellationToken);

        if (!caseExists)
        {
            return OperationResult<Guid>.NotFound("Crime case not found.");
        }

        var investigationId = await _context.Investigations
            .AsNoTracking()
            .Where(i => i.CrimeCaseId == caseId)
            .Select(i => (Guid?)i.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (investigationId is { } existing)
        {
            return OperationResult<Guid>.Success(existing);
        }

        var investigation = new Investigation
        {
            CrimeCaseId = caseId,
            Title = "Investigation",
            StartedAt = DateTime.UtcNow,
            LeadInvestigatorId = actorUserId,
            Status = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Investigations.Add(investigation);
        await _context.SaveChangesAsync(cancellationToken);

        return OperationResult<Guid>.Success(investigation.Id);
    }

    // ------------------------------------------------------------- helpers

    private static IReadOnlyDictionary<string, string[]> ToErrorDictionary(
        Dictionary<string, List<string>> errors) =>
        errors.ToDictionary(e => e.Key, e => e.Value.ToArray());

    private static int ResolveEvidenceType(string value) => value switch
    {
        "Physical" => 1,
        "Digital" => 2,
        "Documentary" => 3,
        "Biological" => 4,
        "Ballistic" => 5,
        "Testimony" => 6,
        _ => 1
    };

    private static int ResolveNoteType(string value) => value switch
    {
        "Observation" => 1,
        "Analysis" => 2,
        "Interview" => 3,
        "Action" => 4,
        _ => 1
    };

    private static int ResolveTimelineEventType(string value) => value switch
    {
        "Incident" => 1,
        "Report" => 2,
        "Warrant" => 3,
        "Interview" => 4,
        "Forensic" => 5,
        "Court" => 6,
        _ => 1
    };

    // Enum display names for the int-coded legacy columns.
    private static string EvidenceTypeName(int value) => value switch
    {
        1 => "Physical",
        2 => "Digital",
        3 => "Documentary",
        4 => "Biological",
        5 => "Ballistic",
        6 => "Testimony",
        _ => "Other"
    };

    private static string EvidenceStatusName(int value) => value switch
    {
        1 => "Collected",
        2 => "In analysis",
        3 => "Submitted",
        4 => "Admitted",
        5 => "Released",
        _ => "Unknown"
    };

    private static string NoteTypeName(int value) => value switch
    {
        1 => "Observation",
        2 => "Analysis",
        3 => "Interview",
        4 => "Action",
        _ => "Note"
    };

    private static string TimelineEventTypeName(int value) => value switch
    {
        1 => "Incident",
        2 => "Report",
        3 => "Warrant",
        4 => "Interview",
        5 => "Forensic",
        6 => "Court",
        _ => "Event"
    };
}