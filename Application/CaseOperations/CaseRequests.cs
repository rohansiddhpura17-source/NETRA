namespace NETRA.Application.CaseOperations;

/// <summary>Payload for registering a new crime case.</summary>
public sealed class CreateCaseRequest
{
    public string? CaseNumber { get; init; }

    public string Title { get; init; } = string.Empty;

    public string? Description { get; init; }

    public Guid CrimeTypeId { get; init; }

    public string Status { get; init; } = string.Empty;

    public string Severity { get; init; } = string.Empty;

    public DateTime IncidentDate { get; init; }

    public Guid? DistrictId { get; init; }

    public string? Address { get; init; }

    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    public Guid? InvestigatorId { get; init; }

    public string? ModusOperandi { get; init; }
}

/// <summary>Payload for editing an existing crime case. Null means "leave unchanged".</summary>
public sealed class UpdateCaseRequest
{
    public string Title { get; init; } = string.Empty;

    public string? Description { get; init; }

    public Guid CrimeTypeId { get; init; }

    public string Status { get; init; } = string.Empty;

    public string Severity { get; init; } = string.Empty;

    public DateTime IncidentDate { get; init; }

    public string? ModusOperandi { get; init; }
}

public sealed class AssignInvestigatorRequest
{
    public Guid? InvestigatorId { get; init; }
}

public sealed class AddPersonToCaseRequest
{
    /// <summary>Existing person to associate. Persons are never created implicitly.</summary>
    public Guid PersonId { get; init; }

    public string Role { get; init; } = string.Empty;

    public string? Notes { get; init; }
}

public sealed class AddLocationToCaseRequest
{
    /// <summary>Existing location to associate. Locations are never created implicitly.</summary>
    public Guid LocationId { get; init; }

    public string? LocationRole { get; init; }

    public string? Notes { get; init; }
}

public sealed class AddEvidenceRequest
{
    public string Title { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string EvidenceType { get; init; } = string.Empty;

    public DateTime? CollectedAt { get; init; }

    public string? CollectionLocation { get; init; }

    public string? ChainOfCustody { get; init; }
}

public sealed class AddEvidenceFileRequest
{
    /// <summary>Original client-supplied file name. Never trusted for the stored path.</summary>
    public string OriginalFileName { get; init; } = string.Empty;

    public string ContentType { get; init; } = string.Empty;

    public long Length { get; init; }

    public Stream Content { get; init; } = Stream.Null;
}

public sealed class AddInvestigationNoteRequest
{
    public string Content { get; init; } = string.Empty;

    public string NoteType { get; init; } = "Observation";

    public bool IsPrivate { get; init; }
}

public sealed class AddTimelineEventRequest
{
    public string Title { get; init; } = string.Empty;

    public string? Description { get; init; }

    public DateTime EventDateTime { get; init; }

    public string EventType { get; init; } = "Incident";

    public string? Location { get; init; }
}

/// <summary>Case-change record surfaced to the edit view.</summary>
public sealed class CaseChangeDto
{
    public Guid Id { get; init; }

    public string Field { get; init; } = string.Empty;

    public string? OldValue { get; init; }

    public string? NewValue { get; init; }

    public string Action { get; init; } = string.Empty;

    public DateTime Timestamp { get; init; }

    public string UserName { get; init; } = string.Empty;
}