using NETRA.Application.Abstractions;
using NETRA.Domain.Enums;

namespace NETRA.Application.Reports;

/// <summary>
/// Reports &amp; intelligence summaries. The service owns persistence and the
/// document assembly; every analytical number is obtained from the existing
/// analytics, risk, dashboard and alert services so no calculation is duplicated
/// here. Case-scoped reads respect the caller's analytical authorisation.
/// </summary>
public interface IReportService
{
    /// <summary>
    /// Lists reports. When <paramref name="allowAnalytical"/> is false, analytical report
    /// rows are excluded from the query entirely rather than hidden in the view, so an
    /// unauthorised reader never sees that such a report exists.
    /// </summary>
    Task<PagedReportsDto> ListAsync(
        ReportFilterCriteria criteria, bool allowAnalytical,
        CancellationToken cancellationToken = default);

    Task<ReportSummaryDto> GetSummaryAsync(
        bool allowAnalytical, CancellationToken cancellationToken = default);

    /// <summary>Bounded case picker options for the filters and the generation form.</summary>
    Task<IReadOnlyList<ReportCaseOptionDto>> GetCaseOptionsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebuilds the report document from its stored parameters. Returns null when the
    /// report does not exist or when it is analytical and <paramref name="allowAnalytical"/>
    /// is false — the caller then answers with the same not-found result, so the
    /// distinction never leaks which reports exist.
    /// </summary>
    Task<ReportDocumentDto?> GetDocumentAsync(
        Guid reportId, Guid actorUserId, bool allowAnalytical,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Produces and persists a system-generated report from live NETRA data.
    /// <paramref name="allowAnalytical"/> must reflect the caller's authorisation: an
    /// analytical type requested without it is refused rather than silently downgraded.
    /// </summary>
    Task<OperationResult<Guid>> GenerateAsync(
        GenerateReportRequest request, Guid actorUserId, bool allowAnalytical,
        CancellationToken cancellationToken = default);

    /// <summary>Creates an analyst-authored report. Server-side validation only.</summary>
    Task<OperationResult<Guid>> CreateAsync(
        CreateAnalystReportRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Edits an analyst-authored report. System-generated reports are immutable.</summary>
    Task<OperationResult> EditAsync(
        Guid reportId, EditAnalystReportRequest request, Guid actorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Archives a report. Content is preserved; the report leaves the working set.</summary>
    Task<OperationResult> ArchiveAsync(
        Guid reportId, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Server-generated CSV of the report document (metadata, metrics, findings and
    /// section tables). No client-side library is involved.
    /// </summary>
    Task<(byte[] Content, string FileName)?> ExportCsvAsync(
        Guid reportId, Guid actorUserId, bool allowAnalytical,
        CancellationToken cancellationToken = default);
}
