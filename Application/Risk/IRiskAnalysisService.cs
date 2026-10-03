using NETRA.Application.Analytics;

namespace NETRA.Application.Risk;

/// <summary>
/// Computes area-level analytical indicators from recorded case data. Results are
/// decision-support measurements, not predictions. The same data and window always
/// produce the same numbers.
/// </summary>
public interface IRiskAnalysisService
{
    /// <summary>
    /// Read-only analysis for the Risk page. The default window is the trailing
    /// 30 days; the comparison period is the 30 days before that.
    /// </summary>
    Task<RiskAnalysisDto> GetAnalysisAsync(
        AnalyticsFilterCriteria criteria,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recomputes the indicators and persists them: one area-level RiskAssessment
    /// per scored district (updated in place on re-runs) plus any newly detected
    /// anomalies. Anomalies already open for the same district and type are not
    /// duplicated.
    /// </summary>
    Task<RiskAssessmentRunResultDto> RunAssessmentAsync(
        AnalyticsFilterCriteria criteria,
        CancellationToken cancellationToken = default);
}
