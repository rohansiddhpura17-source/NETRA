using NETRA.Application.Abstractions;

namespace NETRA.Application.CaseOperations;

/// <summary>
/// Application service for core crime case operations. Declared in the Application
/// layer and implemented in Infrastructure so controllers depend on this contract
/// rather than on EF Core.
/// </summary>
public interface ICaseService
{
    // Reads
    Task<CaseListResultDto> SearchCasesAsync(CaseFilterCriteria criteria, CancellationToken cancellationToken = default);

    Task<CaseDetailDto?> GetCaseDetailsAsync(Guid caseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the case behind an investigation id, so the workspace URL works from
    /// either identifier.
    /// </summary>
    Task<CaseDetailDto?> GetCaseByInvestigationIdAsync(Guid investigationId, CancellationToken cancellationToken = default);

    Task<CaseLookupDto> GetLookupsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CaseChangeDto>> GetCaseHistoryAsync(Guid caseId, CancellationToken cancellationToken = default);

    // Case writes
    Task<OperationResult<Guid>> CreateCaseAsync(CreateCaseRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<OperationResult> UpdateCaseAsync(Guid caseId, UpdateCaseRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<OperationResult> AssignInvestigatorAsync(Guid caseId, AssignInvestigatorRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    // Person association
    Task<OperationResult<Guid>> AddPersonToCaseAsync(Guid caseId, AddPersonToCaseRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<OperationResult> RemovePersonFromCaseAsync(Guid caseId, Guid casePersonId, Guid actorUserId, CancellationToken cancellationToken = default);

    // Location association
    Task<OperationResult<Guid>> AddLocationToCaseAsync(Guid caseId, AddLocationToCaseRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<OperationResult> RemoveLocationFromCaseAsync(Guid caseId, Guid caseLocationId, Guid actorUserId, CancellationToken cancellationToken = default);

    // Evidence
    Task<OperationResult<Guid>> AddEvidenceAsync(Guid caseId, AddEvidenceRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<OperationResult<Guid>> AttachEvidenceFileAsync(Guid caseId, Guid evidenceId, AddEvidenceFileRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    // Investigation record
    Task<OperationResult<Guid>> AddInvestigationNoteAsync(Guid caseId, AddInvestigationNoteRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<OperationResult<Guid>> AddTimelineEventAsync(Guid caseId, AddTimelineEventRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a case has an investigation workspace and returns its id.
    /// </summary>
    Task<OperationResult<Guid>> EnsureInvestigationAsync(Guid caseId, Guid actorUserId, CancellationToken cancellationToken = default);
}