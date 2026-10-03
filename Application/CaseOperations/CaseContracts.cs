using NETRA.Application.Abstractions;

namespace NETRA.Application.CaseOperations;

/// <summary>Filters applied server-side to the crime case list.</summary>
public sealed class CaseFilterCriteria
{
    public string? Search { get; init; }

    public Guid? CrimeTypeId { get; init; }

    public Guid? DistrictId { get; init; }

    public string? Status { get; init; }

    public string? Severity { get; init; }

    public DateTime? IncidentDateFrom { get; init; }

    public DateTime? IncidentDateTo { get; init; }

    public string SortBy { get; init; } = CaseSortOptions.NewestFirst;

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 25;

    public bool HasAnyFilter =>
        !string.IsNullOrWhiteSpace(Search)
        || CrimeTypeId.HasValue
        || DistrictId.HasValue
        || !string.IsNullOrWhiteSpace(Status)
        || !string.IsNullOrWhiteSpace(Severity)
        || IncidentDateFrom.HasValue
        || IncidentDateTo.HasValue;
}

public static class CaseSortOptions
{
    public const string NewestFirst = "newest";
    public const string OldestFirst = "oldest";
    public const string CaseNumberAsc = "caseNumber";
    public const string SeverityDesc = "severity";
}

/// <summary>One row of the crime case list. Projected directly from the database.</summary>
public sealed class CaseListItemDto
{
    public Guid Id { get; init; }

    public string CaseNumber { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string CrimeTypeName { get; init; } = string.Empty;

    public DateTime IncidentDate { get; init; }

    public string? DistrictName { get; init; }

    public string Status { get; init; } = string.Empty;

    public string Severity { get; init; } = string.Empty;

    public string? InvestigatorName { get; init; }

    public int PersonCount { get; init; }

    public int EvidenceCount { get; init; }
}

public sealed class CaseListResultDto
{
    public PagedResult<CaseListItemDto> Page { get; init; } = PagedResult<CaseListItemDto>.Empty(1, 25);

    public CaseFilterCriteria Criteria { get; init; } = new();
}

/// <summary>Lightweight reference data used to populate filter dropdowns.</summary>
public sealed class CaseLookupDto
{
    public IReadOnlyList<LookupItem> CrimeTypes { get; init; } = Array.Empty<LookupItem>();

    public IReadOnlyList<LookupItem> Districts { get; init; } = Array.Empty<LookupItem>();

    public IReadOnlyList<LookupItem> Investigators { get; init; } = Array.Empty<LookupItem>();

    public IReadOnlyList<LookupItem> Persons { get; init; } = Array.Empty<LookupItem>();

    public IReadOnlyList<LookupItem> Locations { get; init; } = Array.Empty<LookupItem>();
}

public sealed class LookupItem
{
    public Guid Id { get; init; }

    public string Label { get; init; } = string.Empty;
}

public sealed class CaseDetailDto
{
    public Guid Id { get; init; }

    public string CaseNumber { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string CrimeTypeName { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string Severity { get; init; } = string.Empty;

    public DateTime IncidentDate { get; init; }

    public DateTime? ReportedDate { get; init; }

    public DateTime? ClosedDate { get; init; }

    public DateTime CreatedAt { get; init; }

    public string? DistrictName { get; init; }

    public string? PoliceStationName { get; init; }

    public string? InvestigatorName { get; init; }

    public string? InvestigatorBadge { get; init; }

    public string CreatedByName { get; init; } = string.Empty;

    public string? ModusOperandi { get; init; }

    public Guid? InvestigationId { get; init; }

    public IReadOnlyList<CasePersonDto> Persons { get; init; } = Array.Empty<CasePersonDto>();

    public IReadOnlyList<CaseLocationDto> Locations { get; init; } = Array.Empty<CaseLocationDto>();

    public IReadOnlyList<EvidenceDto> Evidence { get; init; } = Array.Empty<EvidenceDto>();

    public IReadOnlyList<InvestigationNoteDto> Notes { get; init; } = Array.Empty<InvestigationNoteDto>();

    public IReadOnlyList<TimelineEventDto> Timeline { get; init; } = Array.Empty<TimelineEventDto>();
}

public sealed class CasePersonDto
{
    public Guid Id { get; init; }

    public Guid PersonId { get; init; }

    public string FullName { get; init; } = string.Empty;

    public string? NationalIdNumber { get; init; }

    public string? DateOfBirth { get; init; }

    public string Role { get; init; } = string.Empty;

    public string? Notes { get; init; }

    public DateTime AssociatedAt { get; init; }
}

public sealed class CaseLocationDto
{
    public Guid Id { get; init; }

    public Guid LocationId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Address { get; init; }

    public string? DistrictName { get; init; }

    public string? PoliceStationName { get; init; }

    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    public string? LocationRole { get; init; }

    public string? Notes { get; init; }
}

public sealed class EvidenceDto
{
    public Guid Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string EvidenceType { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public DateTime? CollectedAt { get; init; }

    public string? CollectedByName { get; init; }

    public string? ChainOfCustody { get; init; }

    public IReadOnlyList<EvidenceFileDto> Files { get; set; } = Array.Empty<EvidenceFileDto>();
}

public sealed class EvidenceFileDto
{
    public Guid Id { get; init; }

    public string FileName { get; init; } = string.Empty;

    public string ContentType { get; init; } = string.Empty;

    public long FileSizeBytes { get; init; }

    public string? ChecksumSha256 { get; init; }

    public DateTime UploadedAt { get; init; }

    public string UploadedByName { get; init; } = string.Empty;
}

public sealed class InvestigationNoteDto
{
    public Guid Id { get; init; }

    public string Content { get; init; } = string.Empty;

    public string NoteType { get; init; } = string.Empty;

    public DateTime CreatedAt { get; init; }

    public string AuthorName { get; init; } = string.Empty;

    public bool IsPrivate { get; init; }
}

public sealed class TimelineEventDto
{
    public Guid Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string? Description { get; init; }

    public DateTime EventDateTime { get; init; }

    public string EventType { get; init; } = string.Empty;

    public string? Location { get; init; }

    public string AuthorName { get; init; } = string.Empty;
}