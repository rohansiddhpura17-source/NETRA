using Microsoft.EntityFrameworkCore;
using NETRA.Application.Abstractions;
using NETRA.Application.Analytics;
using NETRA.Application.Risk;
using NETRA.Domain.Cases;
using NETRA.Domain.Enums;
using NETRA.Domain.Intelligence;
using NETRA.Infrastructure.Data;

namespace NETRA.Infrastructure.Services;

/// <summary>
/// <h2>Analytical method (deterministic and explainable)</h2>
/// <para>
/// For each district the service counts cases located there via their "Incident"
/// case-location within the analysis window (default: trailing 30 days) and within
/// the immediately preceding comparison period of equal length. A 0–100 composite
/// score is the sum of four documented, additive components:
/// </para>
/// <list type="number">
///   <item>Volume: 4 points per case in the window, capped at 40.</item>
///   <item>Severity mix: 3 points per high-severity and 6 per critical-severity case, capped at 35.</item>
///   <item>Change vs baseline: up to 15 points, scaled linearly from percentage growth over the
///   comparison period (100% growth earns the full 15; a decrease earns 0). No baseline with new
///   activity earns the full 15.</item>
///   <item>Concentration: up to 10 points, scaled from the leading crime type's share of the window.</item>
/// </list>
/// <para>
/// Levels: Critical ≥ 80, High ≥ 60, Elevated ≥ 40, Moderate ≥ 20, otherwise Low.
/// Districts whose combined window+baseline sample is below <see cref="MinimumSample"/>
/// are reported as insufficient data and never receive an invented score. Identical
/// input data always produces identical output. Results are decision-support
/// indicators, not predictions of future crime and not statements about individuals.
/// </para>
/// <para>
/// Filtering, projection and row de-duplication are translated to SQL with
/// <c>AsNoTracking</c>; the result is the bounded set of distinct case rows inside
/// the two windows. Weekly bucketing and composition arithmetic then run over
/// those rows, which keeps the queries simple and translatable on any provider.
/// </para>
/// </summary>
public sealed class RiskAnalysisService : IRiskAnalysisService
{
    /// <summary>Default analysis window length when no dates are supplied.</summary>
    public const int DefaultWindowDays = 30;

    /// <summary>Cases required (window + comparison) before a score is honest.</summary>
    public const int MinimumSample = 3;

    public const decimal VolumePointsPerCase = 4m;
    public const decimal VolumeCap = 40m;
    public const decimal HighSeverityPoints = 3m;
    public const decimal CriticalSeverityPoints = 6m;
    public const decimal SeverityCap = 35m;
    public const decimal ChangeCap = 15m;

    /// <summary>Percentage growth over the baseline that earns the full change component.</summary>
    public const decimal FullChangeGrowthPercent = 100m;

    public const decimal ConcentrationCap = 10m;

    private readonly NetraDbContext _context;
    private readonly IAnomalyDetector _detector;
    private readonly TimeProvider _timeProvider;
    private readonly IAuditActorAccessor _actor;
    private readonly IAuditService _audit;

    public RiskAnalysisService(
        NetraDbContext context,
        IAnomalyDetector detector,
        TimeProvider timeProvider,
        IAuditActorAccessor actor,
        IAuditService audit)
    {
        _context = context;
        _detector = detector;
        _timeProvider = timeProvider;
        _actor = actor;
        _audit = audit;
    }

    public async Task<RiskAnalysisDto> GetAnalysisAsync(
        AnalyticsFilterCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        var today = _timeProvider.GetUtcNow().UtcDateTime.Date;
        var dateTo = (criteria.DateTo ?? today).Date;
        var dateFrom = (criteria.DateFrom ?? dateTo.AddDays(-(DefaultWindowDays - 1))).Date;

        if (dateFrom > dateTo)
        {
            throw new ArgumentException("Date From must be on or before Date To.", nameof(criteria));
        }

        var windowDays = (dateTo - dateFrom).Days + 1;
        var comparisonTo = dateFrom.AddDays(-1);
        var comparisonFrom = comparisonTo.AddDays(-(windowDays - 1));
        var weekCount = (windowDays / 7) + 1;

        // ---- SQL-translated fetch (filter + projection + distinct rows) -------------
        // The projection is an anonymous shape (records with positional constructors
        // are not translatable by every provider); rows become RiskRow records
        // client-side, after the bounded, distinct window set has been materialised.
        var currentRows = (await WindowedCases(dateFrom, dateTo, criteria)
            .SelectMany(c => c.CaseLocations
                .Where(cl => cl.LocationRole == CaseService.IncidentLocationRole
                             && cl.Location != null
                             && cl.Location.DistrictId != null)
                .Select(cl => new
                {
                    CaseId = c.Id,
                    DistrictId = cl.Location!.DistrictId!.Value,
                    DistrictName = cl.Location.District!.Name,
                    DistrictCode = cl.Location.District!.Code,
                    c.Severity,
                    c.IncidentDate,
                    TypeName = c.CrimeType.Name
                }))
            .Distinct()
            .ToListAsync(cancellationToken))
            .Select(x => new RiskRow(
                x.CaseId, x.DistrictId, x.DistrictName, x.DistrictCode,
                x.Severity, x.IncidentDate, x.TypeName))
            .ToList();

        var baselineRows = (await WindowedCases(comparisonFrom, comparisonTo, criteria)
            .SelectMany(c => c.CaseLocations
                .Where(cl => cl.LocationRole == CaseService.IncidentLocationRole
                             && cl.Location != null
                             && cl.Location.DistrictId != null)
                .Select(cl => new
                {
                    CaseId = c.Id,
                    DistrictId = cl.Location!.DistrictId!.Value,
                    DistrictName = cl.Location.District!.Name,
                    DistrictCode = cl.Location.District!.Code,
                    c.Severity,
                    c.IncidentDate,
                    TypeName = c.CrimeType.Name
                }))
            .Distinct()
            .ToListAsync(cancellationToken))
            .Select(x => new RiskRow(
                x.CaseId, x.DistrictId, x.DistrictName, x.DistrictCode,
                x.Severity, x.IncidentDate, x.TypeName))
            .ToList();

        // ---- grouping over the bounded, distinct window rows -------------------------
        var currentByDistrict = currentRows
            .GroupBy(x => new { x.DistrictId, x.DistrictName, x.DistrictCode })
            .Select(g => new
            {
                g.Key.DistrictId,
                g.Key.DistrictName,
                g.Key.DistrictCode,
                Total = g.Select(x => x.CaseId).Distinct().Count(),
                High = g.Count(x => x.Severity == CaseSeverity.High),
                Critical = g.Count(x => x.Severity == CaseSeverity.Critical)
            })
            .ToList();

        var baselineByDistrict = baselineRows
            .GroupBy(x => new { x.DistrictId, x.DistrictName, x.DistrictCode })
            .Select(g => new
            {
                g.Key.DistrictId,
                g.Key.DistrictName,
                g.Key.DistrictCode,
                Total = g.Select(x => x.CaseId).Distinct().Count(),
                HighSeverity = g.Count(x => x.Severity == CaseSeverity.High || x.Severity == CaseSeverity.Critical)
            })
            .ToList();

        var currentWeekly = currentRows
            .GroupBy(x => new { x.DistrictId, Week = (x.IncidentDate - dateFrom).Days / 7 })
            .Select(g => new { g.Key.DistrictId, g.Key.Week, Count = g.Select(x => x.CaseId).Distinct().Count() })
            .ToList();

        var currentByType = currentRows
            .GroupBy(x => new { x.DistrictId, x.TypeName })
            .Select(g => new { g.Key.DistrictId, g.Key.TypeName, Count = g.Select(x => x.CaseId).Distinct().Count() })
            .ToList();

        var currentByTypeWeek = currentRows
            .GroupBy(x => new { x.DistrictId, x.TypeName, Week = (x.IncidentDate - dateFrom).Days / 7 })
            .Select(g => new
            {
                g.Key.DistrictId,
                g.Key.TypeName,
                g.Key.Week,
                Count = g.Select(x => x.CaseId).Distinct().Count()
            })
            .ToList();

        var totalCasesInWindow = currentRows
            .Select(x => x.CaseId)
            .Distinct()
            .Count();

        // ---- assembly (small aggregated sets only) -------------------------------
        var districtKeys = currentByDistrict
            .Select(x => (x.DistrictId, x.DistrictName, x.DistrictCode))
            .Concat(baselineByDistrict.Select(x => (x.DistrictId, x.DistrictName, x.DistrictCode)))
            .Distinct()
            .OrderBy(x => x.DistrictName)
            .ToList();

        var indicators = new List<RiskIndicatorDto>();
        var anomalies = new List<AnomalyCandidateDto>();

        foreach (var key in districtKeys)
        {
            var current = currentByDistrict.FirstOrDefault(x => x.DistrictId == key.DistrictId);
            var baseline = baselineByDistrict.FirstOrDefault(x => x.DistrictId == key.DistrictId);
            var currentCount = current?.Total ?? 0;
            var baselineCount = baseline?.Total ?? 0;
            var high = current?.High ?? 0;
            var critical = current?.Critical ?? 0;
            var baselineHighSeverity = baseline?.HighSeverity ?? 0;

            var types = currentByType
                .Where(x => x.DistrictId == key.DistrictId)
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.TypeName)
                .ToList();

            var leading = types.FirstOrDefault();
            var share = currentCount == 0 || leading is null
                ? 0m
                : Math.Round(leading.Count * 100m / currentCount, 1);

            var weekly = BuildWeeklySeries(
                currentWeekly.Where(w => w.DistrictId == key.DistrictId).Select(w => (w.Week, w.Count)),
                weekCount);

            var weeklyByType = types.ToDictionary(
                t => t.TypeName,
                t => (IReadOnlyList<int>)BuildWeeklySeries(
                    currentByTypeWeek
                        .Where(w => w.DistrictId == key.DistrictId && w.TypeName == t.TypeName)
                        .Select(w => (w.Week, w.Count)),
                    weekCount));

            var insufficient = currentCount + baselineCount < MinimumSample;
            var changePercent = baselineCount > 0
                ? Math.Round((currentCount - baselineCount) * 100m / baselineCount, 1)
                : (decimal?)null;

            decimal? score = null;
            var level = 0;
            string levelLabel;
            string explanation;

            if (insufficient)
            {
                levelLabel = "Insufficient data";
                explanation = $"Only {currentCount} case{(currentCount == 1 ? "" : "s")} in the window and " +
                    $"{baselineCount} in the comparison period, below the {MinimumSample}-case minimum; " +
                    "no analytical score is calculated rather than inventing one.";
            }
            else
            {
                var volume = Math.Min(VolumeCap, currentCount * VolumePointsPerCase);
                var severity = Math.Min(
                    SeverityCap,
                    (high * HighSeverityPoints) + (critical * CriticalSeverityPoints));
                var change = ChangeComponent(currentCount, baselineCount);
                var concentration = Math.Round(
                    Math.Min(ConcentrationCap, share / 100m * ConcentrationCap), 1);

                score = Math.Round(
                    Math.Clamp(volume + severity + change + concentration, 0m, 100m), 1);
                level = LevelFor(score.Value);
                levelLabel = LevelLabel(level);

                var trendText = changePercent is { } pct
                    ? $"{(pct >= 0 ? "+" : "")}{pct:0.#}% versus the comparison period"
                    : baselineCount == 0
                        ? "no comparison-period activity"
                        : "no change measure available";

                explanation = $"{currentCount} case{(currentCount == 1 ? "" : "s")} in the window versus " +
                    $"{baselineCount} in the comparison period ({trendText}); {high} high and {critical} critical " +
                    $"severity. Components: volume +{volume:0.#}, severity +{severity:0.#}, change +{change:0.#}, " +
                    $"concentration +{concentration:0.#}. Score {score:0.#} of 100.";
            }

            indicators.Add(new RiskIndicatorDto
            {
                DistrictId = key.DistrictId,
                DistrictName = key.DistrictName,
                DistrictCode = key.DistrictCode,
                CurrentCount = currentCount,
                BaselineCount = baselineCount,
                HighSeverityCount = high,
                CriticalSeverityCount = critical,
                ChangePercent = changePercent,
                Score = score,
                Level = level,
                LevelLabel = levelLabel,
                InsufficientData = insufficient,
                WeeklyCounts = weekly,
                LeadingCrimeType = leading?.TypeName,
                LeadingCrimeTypeShare = share,
                Explanation = explanation
            });

            if (!insufficient)
            {
                anomalies.AddRange(_detector.Detect(new AnomalyDetectionInput
                {
                    DistrictId = key.DistrictId,
                    DistrictName = key.DistrictName,
                    CurrentCount = currentCount,
                    BaselineCount = baselineCount,
                    CurrentHighSeverityCount = high + critical,
                    BaselineHighSeverityCount = baselineHighSeverity,
                    LeadingCrimeTypeCount = leading?.Count ?? 0,
                    LeadingCrimeType = leading?.TypeName ?? string.Empty,
                    WeeklyCountsByType = weeklyByType
                }));
            }
        }

        var lastRun = await _context.RiskAssessments
            .AsNoTracking()
            .Where(r => r.DistrictId != null)
            .Select(r => (DateTime?)r.AssessedAt)
            .MaxAsync(cancellationToken);

        return new RiskAnalysisDto
        {
            DateFrom = dateFrom,
            DateTo = dateTo,
            ComparisonFrom = comparisonFrom,
            ComparisonTo = comparisonTo,
            Indicators = indicators
                .OrderByDescending(i => i.Score ?? -1m)
                .ThenBy(i => i.DistrictName)
                .ToList(),
            Anomalies = anomalies
                .OrderByDescending(a => SeverityRank(a.Severity))
                .ThenBy(a => a.DistrictName)
                .ThenBy(a => a.AnomalyType)
                .ToList(),
            TotalCasesInWindow = totalCasesInWindow,
            LastRecordedRunAt = lastRun
        };
    }

    public async Task<RiskAssessmentRunResultDto> RunAssessmentAsync(
        AnalyticsFilterCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        var actorId = _actor.GetUserId();
        if (actorId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "A signed-in user is required to record risk assessments.");
        }

        var analysis = await GetAnalysisAsync(criteria, cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var scored = analysis.Indicators
            .Where(i => i.Score is not null && !i.InsufficientData)
            .ToList();

        // One area assessment per district, refreshed in place on re-runs.
        var existing = (await _context.RiskAssessments
                .Where(r => r.DistrictId != null)
                .ToListAsync(cancellationToken))
            .GroupBy(r => r.DistrictId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.AssessedAt).First());

        var assessmentsSaved = 0;

        foreach (var indicator in scored)
        {
            var title = $"Area risk indicator — {indicator.DistrictName}";
            var summary = BuildAssessmentSummary(indicator, analysis);
            var recommendations = indicator.Level switch
            {
                >= 4 => "Review required: prioritise patrol and investigative attention in this area.",
                3 => "Monitor: schedule a closer review of recent activity in this area.",
                _ => "No action beyond routine monitoring is indicated by this analytical measure."
            };

            if (existing.TryGetValue(indicator.DistrictId, out var assessment))
            {
                assessment.Title = title;
                assessment.Assessment = summary;
                assessment.RiskLevel = indicator.Level;
                assessment.RiskScore = indicator.Score!.Value;
                assessment.Factors = indicator.Explanation;
                assessment.Recommendations = recommendations;
                assessment.AssessedAt = now;
                assessment.AssessedById = actorId;
                assessment.UpdatedAt = now;

                _audit.Record(
                    AuditEntityTypeNames.RiskAssessment,
                    assessment.Id,
                    AuditActionNames.Edited,
                    actorId,
                    propertyName: "RiskScore",
                    newValue: $"{indicator.Score:0.#} ({indicator.LevelLabel})");
            }
            else
            {
                var created = new RiskAssessment
                {
                    DistrictId = indicator.DistrictId,
                    Title = title,
                    Assessment = summary,
                    RiskLevel = indicator.Level,
                    RiskScore = indicator.Score!.Value,
                    Factors = indicator.Explanation,
                    Recommendations = recommendations,
                    AssessedAt = now,
                    AssessedById = actorId,
                    CreatedAt = now
                };

                _context.RiskAssessments.Add(created);
                existing[indicator.DistrictId] = created;

                _audit.Record(
                    AuditEntityTypeNames.RiskAssessment,
                    created.Id,
                    AuditActionNames.Created,
                    actorId,
                    propertyName: "RiskScore",
                    newValue: $"{indicator.Score:0.#} ({indicator.LevelLabel})");
            }

            assessmentsSaved++;
        }

        // Anomalies already open for the same district and type are not duplicated.
        var openKeys = new HashSet<(Guid? EntityId, int Type, string Title)>(
            (await _context.Anomalies
                    .AsNoTracking()
                    .Where(a => a.Status == 1 && a.RelatedEntityType == "District")
                    .Select(a => new { a.RelatedEntityId, a.AnomalyType, a.Title })
                    .ToListAsync(cancellationToken))
                .Select(a => (a.RelatedEntityId, a.AnomalyType, a.Title)));

        var anomaliesSaved = 0;
        var skipped = 0;

        foreach (var candidate in analysis.Anomalies)
        {
            var key = (candidate.DistrictId, candidate.AnomalyType, candidate.Title);

            if (!openKeys.Add(key))
            {
                skipped++;
                continue;
            }

            var anomaly = new Anomaly
            {
                Title = candidate.Title,
                Description = candidate.Description,
                Severity = ParseSeverity(candidate.Severity),
                AnomalyType = candidate.AnomalyType,
                RelatedEntityId = candidate.DistrictId,
                RelatedEntityType = "District",
                DetectedPatterns = AnomalyTypes.Label(candidate.AnomalyType),
                ConfidenceScore = candidate.Confidence,
                DetectedAt = now,
                DetectedById = actorId,
                Status = 1,
                CreatedAt = now
            };

            _context.Anomalies.Add(anomaly);

            _audit.Record(
                AuditEntityTypeNames.Anomaly,
                anomaly.Id,
                AuditActionNames.Created,
                actorId,
                newValue: candidate.Title);

            anomaliesSaved++;
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new RiskAssessmentRunResultDto
        {
            AssessmentsSaved = assessmentsSaved,
            AnomaliesSaved = anomaliesSaved,
            AnomaliesSkippedAsOpen = skipped,
            RunAt = now
        };
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Cases inside an inclusive date range with the shared
    /// <see cref="CaseQueryFilters"/> logic, so every module filters identically.
    /// </summary>
    private IQueryable<CrimeCase> WindowedCases(
        DateTime dateFrom,
        DateTime dateToInclusive,
        AnalyticsFilterCriteria criteria) =>
        CaseQueryFilters.Apply(
            _context.CrimeCases.AsNoTracking(), criteria, dateFrom, dateToInclusive);

    private static IReadOnlyList<int> BuildWeeklySeries(
        IEnumerable<(int Week, int Count)> rows,
        int weekCount)
    {
        var series = new int[Math.Max(weekCount, 1)];

        foreach (var (week, count) in rows)
        {
            if (week >= 0 && week < series.Length)
            {
                series[week] = count;
            }
        }

        return series;
    }

    private static decimal ChangeComponent(int current, int baseline)
    {
        if (baseline == 0)
        {
            return current > 0 ? ChangeCap : 0m;
        }

        var growthPercent = (current - baseline) * 100m / baseline;
        var scaled = growthPercent / FullChangeGrowthPercent * ChangeCap;
        return Math.Round(Math.Clamp(scaled, 0m, ChangeCap), 1);
    }

    private static int LevelFor(decimal score) => score switch
    {
        >= 80m => 5,
        >= 60m => 4,
        >= 40m => 3,
        >= 20m => 2,
        _ => 1
    };

    private static string LevelLabel(int level) => level switch
    {
        5 => "Critical",
        4 => "High",
        3 => "Elevated",
        2 => "Moderate",
        1 => "Low",
        _ => "Insufficient data"
    };

    private static int SeverityRank(string severity) => severity switch
    {
        "Critical" => 4,
        "High" => 3,
        "Medium" => 2,
        _ => 1
    };

    private static AnomalySeverity ParseSeverity(string severity) => severity switch
    {
        "Critical" => AnomalySeverity.Critical,
        "High" => AnomalySeverity.High,
        "Low" => AnomalySeverity.Low,
        _ => AnomalySeverity.Medium
    };

    private static string BuildAssessmentSummary(RiskIndicatorDto indicator, RiskAnalysisDto analysis) =>
        $"Area risk indicator for {indicator.DistrictName}. Analysis window " +
        $"{analysis.DateFrom:dd MMM yyyy} to {analysis.DateTo:dd MMM yyyy} compared with " +
        $"{analysis.ComparisonFrom:dd MMM yyyy} to {analysis.ComparisonTo:dd MMM yyyy}. " +
        $"{indicator.Explanation} This is an analytical indicator for decision support; " +
        "it is not a prediction of future crime.";

    private sealed record RiskRow(
        Guid CaseId,
        Guid DistrictId,
        string DistrictName,
        string DistrictCode,
        CaseSeverity Severity,
        DateTime IncidentDate,
        string TypeName);
}
