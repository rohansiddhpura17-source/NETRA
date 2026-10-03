using NETRA.Application.Risk;

namespace NETRA.Infrastructure.Services;

/// <summary>
/// Deterministic anomaly rules. Every rule compares the analysis window against
/// the immediately preceding baseline of equal length; a merely large number never
/// raises an anomaly on its own. The same input always yields the same output.
/// </summary>
public sealed class AnomalyDetector : IAnomalyDetector
{
    /// <summary>Minimum cases in the window before a comparison is meaningful.</summary>
    public const int MinimumSample = 4;

    /// <summary>A window is anomalous when it reaches 150% of its baseline.</summary>
    public const decimal VolumeSpikeRatio = 1.5m;

    /// <summary>Concentration share (0–1) treated as unusual, with a minimum count.</summary>
    public const decimal ConcentrationShare = 0.6m;

    public const int ConcentrationMinimum = 5;

    /// <summary>Severity shift: at least double the baseline high/critical count.</summary>
    public const decimal SeverityShiftRatio = 2.0m;

    public const int SeverityShiftMinimum = 2;

    /// <summary>Recurrence: a type present in at least this many weeks of the window.</summary>
    public const int RecurrenceMinimumWeeks = 3;

    public const int RecurrenceMinimumTotal = 3;

    public IReadOnlyList<AnomalyCandidateDto> Detect(AnomalyDetectionInput input)
    {
        var results = new List<AnomalyCandidateDto>();

        var current = input.CurrentCount;
        var baseline = input.BaselineCount;

        // Rule 1 — volume spike: window reached >= 150% of baseline with a real increase.
        if (current >= MinimumSample && current - baseline >= 2)
        {
            var isSpike = baseline > 0
                ? current >= baseline * VolumeSpikeRatio
                : true;

            if (isSpike)
            {
                var deviation = baseline > 0
                    ? Math.Round((current - baseline) * 100m / baseline, 1)
                    : (decimal?)null;

                var severity = baseline > 0 && current >= baseline * 2 ? "Critical" : "High";

                results.Add(new AnomalyCandidateDto
                {
                    Title = deviation is null
                        ? $"New activity concentrated in {input.DistrictName}"
                        : $"Crime volume up {deviation:0.#}% in {input.DistrictName}",
                    Description = deviation is null
                        ? $"{current} case{(current == 1 ? "" : "s")} recorded in {input.DistrictName} " +
                          "during the analysis window, compared with none in the preceding period of equal length."
                        : $"Crime volume in {input.DistrictName} increased by {deviation:0.#}% compared with the " +
                          $"previous comparison period ({current} cases versus {baseline}).",
                    Severity = severity,
                    AnomalyType = AnomalyTypes.VolumeSpike,
                    DistrictId = input.DistrictId,
                    DistrictName = input.DistrictName,
                    DeviationPercent = deviation,
                    Confidence = Confidence(current, baseline)
                });
            }
        }

        // Rule 2 — severity shift: high/critical cases at least doubled versus baseline.
        var high = input.CurrentHighSeverityCount;
        var baseHigh = input.BaselineHighSeverityCount;
        if (high >= SeverityShiftMinimum && high >= baseHigh * SeverityShiftRatio && high > baseHigh)
        {
            results.Add(new AnomalyCandidateDto
            {
                Title = $"Severity shift in {input.DistrictName}",
                Description = $"High or critical severity cases in {input.DistrictName} rose from {baseHigh} in the " +
                    $"comparison period to {high} in the analysis window.",
                Severity = high >= 4 ? "High" : "Medium",
                AnomalyType = AnomalyTypes.SeverityShift,
                DistrictId = input.DistrictId,
                DistrictName = input.DistrictName,
                DeviationPercent = baseHigh > 0
                    ? Math.Round((high - baseHigh) * 100m / baseHigh, 1)
                    : null,
                Confidence = Confidence(high, baseHigh)
            });
        }

        // Rule 3 — unusual concentration: one crime type dominates the window.
        if (current >= ConcentrationMinimum
            && input.LeadingCrimeTypeCount >= Math.Ceiling(current * ConcentrationShare))
        {
            var share = Math.Round(input.LeadingCrimeTypeCount * 100m / current, 1);
            results.Add(new AnomalyCandidateDto
            {
                Title = $"Unusual concentration of {input.LeadingCrimeType} in {input.DistrictName}",
                Description = $"{input.LeadingCrimeType} accounts for {share:0.#}% of the cases recorded in " +
                    $"{input.DistrictName} during the analysis window ({input.LeadingCrimeTypeCount} of {current}).",
                Severity = share >= 80m && input.LeadingCrimeTypeCount >= 8 ? "High" : "Medium",
                AnomalyType = AnomalyTypes.CrimeConcentration,
                DistrictId = input.DistrictId,
                DistrictName = input.DistrictName,
                DeviationPercent = null,
                Confidence = Math.Round(share / 100m, 3)
            });
        }

        // Rule 4 — recurring pattern: the leading type appears in most weeks of the window.
        if (input.LeadingCrimeTypeCount >= RecurrenceMinimumTotal
            && input.WeeklyCountsByType.TryGetValue(input.LeadingCrimeType, out var weekly))
        {
            var activeWeeks = weekly.Count(c => c > 0);

            if (weekly.Count >= RecurrenceMinimumWeeks && activeWeeks >= RecurrenceMinimumWeeks
                && activeWeeks >= Math.Ceiling(weekly.Count * 0.6m))
            {
                results.Add(new AnomalyCandidateDto
                {
                    Title = $"Recurring {input.LeadingCrimeType} pattern in {input.DistrictName}",
                    Description = $"{input.LeadingCrimeType} was recorded in {activeWeeks} of the last " +
                        $"{weekly.Count} weeks in {input.DistrictName}, a repeating rather than one-off pattern.",
                    Severity = activeWeeks >= 4 && input.LeadingCrimeTypeCount >= 6 ? "Medium" : "Low",
                    AnomalyType = AnomalyTypes.RecurringPattern,
                    DistrictId = input.DistrictId,
                    DistrictName = input.DistrictName,
                    DeviationPercent = null,
                    Confidence = Math.Round(activeWeeks / (decimal)weekly.Count, 3)
                });
            }
        }

        return results;
    }

    /// <summary>
    /// Deterministic confidence in [0, 0.99]: how much of the combined
    /// window+baseline volume falls inside the window.
    /// </summary>
    private static decimal Confidence(int current, int baseline)
    {
        var total = current + baseline;
        if (total == 0)
        {
            return 0m;
        }

        return Math.Min(0.99m, Math.Round(current / (decimal)total, 3));
    }
}
