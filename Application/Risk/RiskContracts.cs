namespace NETRA.Application.Risk;

/// <summary>
/// Area-level analytical indicators for one district. These are decision-support
/// measurements derived from recorded case data — not predictions of future crime
/// and not statements about any individual.
/// </summary>
public sealed class RiskIndicatorDto
{
    public required Guid DistrictId { get; init; }

    public required string DistrictName { get; init; }

    public required string DistrictCode { get; init; }

    /// <summary>Cases recorded in the analysis window.</summary>
    public int CurrentCount { get; init; }

    /// <summary>Cases recorded in the immediately preceding window of equal length.</summary>
    public int BaselineCount { get; init; }

    public int HighSeverityCount { get; init; }

    public int CriticalSeverityCount { get; init; }

    /// <summary>
    /// Percentage change of volume versus the comparison window. Null when the
    /// baseline is zero (no percentage is defined; the service explains this case
    /// separately rather than inventing a number).
    /// </summary>
    public decimal? ChangePercent { get; init; }

    /// <summary>
    /// Composite 0–100 analytical score, or null when the sample is too small to
    /// explain honestly (see <see cref="InsufficientData"/>).
    /// </summary>
    public decimal? Score { get; init; }

    /// <summary>1 Low, 2 Moderate, 3 Elevated, 4 High, 5 Critical; 0 when data is insufficient.</summary>
    public int Level { get; init; }

    public string LevelLabel { get; init; } = string.Empty;

    /// <summary>True when the combined current+baseline sample is below the minimum.</summary>
    public bool InsufficientData { get; init; }

    /// <summary>Weekly case counts across the analysis window, oldest first (sparkline input).</summary>
    public IReadOnlyList<int> WeeklyCounts { get; init; } = Array.Empty<int>();

    /// <summary>Most frequent crime type in the window, if any.</summary>
    public string? LeadingCrimeType { get; init; }

    /// <summary>Share (0–100) of the window's cases belonging to the leading crime type.</summary>
    public decimal LeadingCrimeTypeShare { get; init; }

    /// <summary>Human-readable explanation of how this row's score was formed.</summary>
    public string Explanation { get; init; } = string.Empty;
}

/// <summary>An unusual pattern detected by comparing a window against its baseline.</summary>
public sealed class AnomalyCandidateDto
{
    public required string Title { get; init; }

    /// <summary>Full sentence explaining the detection, including the compared values.</summary>
    public required string Description { get; init; }

    /// <summary>Low / Medium / High / Critical.</summary>
    public required string Severity { get; init; }

    /// <summary>Detection kind; see <see cref="AnomalyTypes"/>.</summary>
    public required int AnomalyType { get; init; }

    public required Guid DistrictId { get; init; }

    public required string DistrictName { get; init; }

    /// <summary>Percentage deviation from the comparison period, when one exists.</summary>
    public decimal? DeviationPercent { get; init; }

    /// <summary>0–1 deterministic confidence derived from the observed ratio.</summary>
    public required decimal Confidence { get; init; }
}

/// <summary>Well-known <see cref="AnomalyCandidateDto.AnomalyType"/> values.</summary>
public static class AnomalyTypes
{
    /// <summary>Volume rose sharply against the previous comparison window.</summary>
    public const int VolumeSpike = 1;

    /// <summary>High/critical severity share rose sharply against the baseline.</summary>
    public const int SeverityShift = 2;

    /// <summary>Most of the district's cases concentrate in a single crime type.</summary>
    public const int CrimeConcentration = 3;

    /// <summary>The same crime type recurs across most weeks of the window.</summary>
    public const int RecurringPattern = 4;

    public static string Label(int type) => type switch
    {
        VolumeSpike => "Volume spike",
        SeverityShift => "Severity shift",
        CrimeConcentration => "Crime concentration",
        RecurringPattern => "Recurring pattern",
        _ => "Unusual activity"
    };
}

/// <summary>Per-district measurements handed to the anomaly detector.</summary>
public sealed class AnomalyDetectionInput
{
    public required Guid DistrictId { get; init; }

    public required string DistrictName { get; init; }

    public required int CurrentCount { get; init; }

    public required int BaselineCount { get; init; }

    public required int CurrentHighSeverityCount { get; init; }

    public required int BaselineHighSeverityCount { get; init; }

    /// <summary>Count of the leading crime type inside the window.</summary>
    public required int LeadingCrimeTypeCount { get; init; }

    public required string LeadingCrimeType { get; init; }

    /// <summary>Weekly counts per crime type (type name → weekly series, oldest week first).</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<int>> WeeklyCountsByType { get; init; }
}

/// <summary>Result of a persisted risk assessment run.</summary>
public sealed class RiskAssessmentRunResultDto
{
    public int AssessmentsSaved { get; init; }

    public int AnomaliesSaved { get; init; }

    public int AnomaliesSkippedAsOpen { get; init; }

    public DateTime RunAt { get; init; }
}

/// <summary>Everything the Risk &amp; Anomaly Analysis page renders.</summary>
public sealed class RiskAnalysisDto
{
    public required DateTime DateFrom { get; init; }

    public required DateTime DateTo { get; init; }

    public required DateTime ComparisonFrom { get; init; }

    public required DateTime ComparisonTo { get; init; }

    public IReadOnlyList<RiskIndicatorDto> Indicators { get; init; } = Array.Empty<RiskIndicatorDto>();

    public IReadOnlyList<AnomalyCandidateDto> Anomalies { get; init; } = Array.Empty<AnomalyCandidateDto>();

    public int TotalCasesInWindow { get; init; }

    /// <summary>When persisted area assessments exist, the most recent run; otherwise null.</summary>
    public DateTime? LastRecordedRunAt { get; init; }
}
