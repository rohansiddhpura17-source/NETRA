using NETRA.Application.Abstractions;

namespace NETRA.Application.BulkImport;

/// <summary>
/// Server-side bulk import pipeline: UPLOAD, VALIDATE, PREVIEW, IMPORT, RESULT.
/// </summary>
/// <remarks>
/// The stage boundaries are deliberate and are what makes the flow safe:
/// <list type="bullet">
///   <item><description><see cref="UploadAsync"/> stores the file and registers a job. It parses nothing and writes no case rows.</description></item>
///   <item><description><see cref="ValidateAsync"/> reads the file and stages one record per row with its outcome. It never writes to the case tables.</description></item>
///   <item><description><see cref="PreviewAsync"/> is read-only apart from its audit entry, so looking at a preview proves nothing was imported.</description></item>
///   <item><description><see cref="ImportAsync"/> re-reads and re-validates the stored file, then writes accepted rows in one transaction.</description></item>
/// </list>
/// Callers pass the acting user explicitly; authorisation to reach these methods is
/// enforced by policy, and ownership of a job is enforced here.
/// </remarks>
public interface IBulkImportService
{
    /// <summary>Column contract, value formats and ceilings an operator needs before writing a file.</summary>
    Task<BulkImportRequirementsDto> GetRequirementsAsync(CancellationToken cancellationToken = default);

    /// <summary>The acting user's most recent jobs, newest first.</summary>
    Task<IReadOnlyList<BulkImportJobDto>> GetRecentJobsAsync(
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the upload itself, stores it through the file storage abstraction and
    /// registers a pending job. No row content is interpreted and no staged rows exist yet.
    /// </summary>
    Task<OperationResult<BulkImportJobDto>> UploadAsync(
        UploadBulkImportRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Job header for a workflow page. Forbidden when the job belongs to someone else.</summary>
    Task<OperationResult<BulkImportJobDto>> GetJobAsync(
        Guid jobId,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Parses and validates the stored file and stages one record per row. Re-running it on
    /// an already validated job returns the persisted outcome instead of rewriting it.
    /// </summary>
    Task<OperationResult<BulkImportValidationDto>> ValidateAsync(
        Guid jobId,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Read-only preview of the rows an import would accept.</summary>
    Task<OperationResult<BulkImportPreviewDto>> PreviewAsync(
        Guid jobId,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the accepted rows into the case register inside a single transaction, after
    /// re-checking the job state and re-validating the stored file.
    /// </summary>
    Task<OperationResult<BulkImportResultDto>> ImportAsync(
        Guid jobId,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Abandons a staged job before it is imported. Stored rows are kept for audit.</summary>
    Task<OperationResult<BulkImportJobDto>> CancelAsync(
        Guid jobId,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Outcome of a finished, failed or cancelled job.</summary>
    Task<OperationResult<BulkImportResultDto>> GetResultAsync(
        Guid jobId,
        Guid actorUserId,
        CancellationToken cancellationToken = default);
}
