namespace NETRA.Application.Risk;

/// <summary>
/// Pure, deterministic anomaly rules. The detector compares a window against its
/// preceding baseline; a high value alone never raises an anomaly.
/// </summary>
public interface IAnomalyDetector
{
    IReadOnlyList<AnomalyCandidateDto> Detect(AnomalyDetectionInput input);
}
