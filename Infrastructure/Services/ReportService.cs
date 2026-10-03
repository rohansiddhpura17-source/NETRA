using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NETRA.Application.Abstractions;
using NETRA.Application.Alerts;
using NETRA.Application.Analytics;
using NETRA.Application.CaseOperations;
using NETRA.Application.Dashboard;
using NETRA.Application.Risk;
using NETRA.Application.Reports;
using NETRA.Domain.Enums;
using NETRA.Domain.Reports;
using NETRA.Infrastructure.Data;

namespace NETRA.Infrastructure.Services;

/// <summary>
/// Reports &amp; intelligence summaries. This service assembles documents; it never
/// recomputes analytics, risk scores or alert counts itself. Every analytical number
/// comes from <see cref="IAnalyticsService"/>, <see cref="IRiskAnalysisService"/>,
/// <see cref="IDashboardService"/> or <see cref="IAlertService"/>, and every case fact
/// from <see cref="ICaseService"/>. Reports persist their parameters rather than a copy
/// of the data, so a reopened report always reflects the current records.
/// </summary>
public sealed class ReportService : IReportService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly NetraDbContext _context;
    private readonly IAuditService _audit;
    private readonly TimeProvider _time;
    private readonly ICaseService _cases;
    private readonly IAnalyticsService _analytics;
    private readonly IRiskAnalysisService _risk;
    private readonly IDashboardService _dashboard;
    private readonly IAlertService _alerts;

    public ReportService(
        NetraDbContext context,
        IAuditService audit,
        TimeProvider time,
        ICaseService cases,
        IAnalyticsService analytics,
        IRiskAnalysisService risk,
        IDashboardService dashboard,
        IAlertService alerts)
    {
        _context = context;
        _audit = audit;
        _time = time;
        _cases = cases;
        _analytics = analytics;
        _risk = risk;
        _dashboard = dashboard;
        _alerts = alerts;
    }

    // --------------------------------------------------------------------- list

    public async Task<PagedReportsDto> ListAsync(
        ReportFilterCriteria criteria, bool allowAnalytical,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, criteria.Page);
        var pageSize = Math.Clamp(criteria.PageSize, 1, ReportLimits.MaxPageSize);

        var query = _context.Reports.AsNoTracking();

        if (!allowAnalytical)
        {
            query = query.Where(r => !ReportTypes.Analytical.Contains(r.ReportType));
        }

        if (!string.IsNullOrWhiteSpace(criteria.Search))
        {
            var term = criteria.Search.Trim().ToLower();
            query = query.Where(r =>
                r.Title.ToLower().Contains(term)
                || (r.Description != null && r.Description.ToLower().Contains(term))
                || (r.RequestedBy.FirstName + " " + r.RequestedBy.LastName).ToLower().Contains(term));
        }

        if (criteria.Type is { } type)
        {
            query = query.Where(r => r.ReportType == type);
        }

        var groupTypes = ReportTypes.TypesIn(criteria.Group);
        if (groupTypes.Length > 0)
        {
            query = query.Where(r => groupTypes.Contains(r.ReportType));
        }

        if (criteria.Source is { } source)
        {
            query = query.Where(r => r.Source == source);
        }

        if (criteria.Status is { } status)
        {
            query = query.Where(r => r.Status == status);
        }

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(r => r.CreatedAt)
            // Timestamps written in one batch can be identical, and SQL Server does not keep
            // tied rows in a stable order across OFFSET/FETCH pages.
            .ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new ReportListRow(
                r.Id, r.Title, r.ReportType, r.Source, r.Status, r.Description,
                r.PeriodFrom, r.PeriodTo, r.CreatedAt, r.GeneratedAt,
                r.RequestedBy.FirstName + " " + r.RequestedBy.LastName,
                r.CrimeCase == null ? null : r.CrimeCase.CaseNumber,
                r.GeneratedFilePath))
            .ToListAsync(cancellationToken);

        return new PagedReportsDto
        {
            Items = rows.Select(r => new ReportListItemDto
            {
                Id = r.Id,
                Title = r.Title,
                Type = r.Type,
                Source = r.Source,
                Status = r.Status,
                Summary = r.Summary,
                PeriodFrom = r.PeriodFrom,
                PeriodTo = r.PeriodTo,
                CreatedAt = r.CreatedAt,
                GeneratedAt = r.GeneratedAt,
                AuthorName = r.AuthorName,
                CaseNumber = r.CaseNumber,
                SectionCount = ParseSectionCount(r.SectionCount),
                CanEdit = r.Source == ReportSource.AnalystAuthored && r.Status != ReportStatus.Archived
            }).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        };
    }

    public async Task<ReportSummaryDto> GetSummaryAsync(
        bool allowAnalytical, CancellationToken cancellationToken = default)
    {
        var reports = _context.Reports.AsNoTracking();

        if (!allowAnalytical)
        {
            reports = reports.Where(r => !ReportTypes.Analytical.Contains(r.ReportType));
        }

        return new ReportSummaryDto
        {
            TotalCount = await reports.CountAsync(cancellationToken),
            SystemGeneratedCount = await reports.CountAsync(
                r => r.Source == ReportSource.SystemGenerated, cancellationToken),
            AnalystAuthoredCount = await reports.CountAsync(
                r => r.Source == ReportSource.AnalystAuthored, cancellationToken),
            AnalyticalCount = allowAnalytical
                ? await reports.CountAsync(
                    r => ReportTypes.Analytical.Contains(r.ReportType), cancellationToken)
                : 0,
            ArchivedCount = await reports.CountAsync(
                r => r.Status == ReportStatus.Archived, cancellationToken)
        };
    }

    public async Task<IReadOnlyList<ReportCaseOptionDto>> GetCaseOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        var cases = await _context.CrimeCases.AsNoTracking()
            .OrderByDescending(c => c.IncidentDate)
            .Take(ReportLimits.MaxCaseOptions)
            .Select(c => new { c.Id, c.CaseNumber })
            .ToListAsync(cancellationToken);

        return cases.Select(c => new ReportCaseOptionDto(c.Id, c.CaseNumber)).ToList();
    }

    // ----------------------------------------------------------------- document

    public async Task<ReportDocumentDto?> GetDocumentAsync(
        Guid reportId, Guid actorUserId, bool allowAnalytical,
        CancellationToken cancellationToken = default)
    {
        var report = await _context.Reports.AsNoTracking()
            .Include(r => r.RequestedBy)
            .Include(r => r.GeneratedBy)
            .Include(r => r.CrimeCase)
            .FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken);

        if (report is null)
        {
            return null;
        }

        if (ReportTypes.IsAnalytical(report.ReportType) && !allowAnalytical)
        {
            // Answered as "not found" so the existence of analytical reports is not leaked.
            _audit.RecordSecurityEvent(
                AuditActionNames.ReportAccessDenied, actorUserId,
                detail: $"Attempted to open analytical report {reportId:N} without analytical authorisation.");
            await _context.SaveChangesAsync(cancellationToken);
            return null;
        }

        var document = await BuildDocumentAsync(report, cancellationToken);

        _audit.Record(
            AuditEntityTypeNames.Report, reportId, AuditActionNames.ReportViewed, actorUserId,
            newValue: Truncate(document.Title, 200));
        await _context.SaveChangesAsync(cancellationToken);

        return document;
    }

    private async Task<ReportDocumentDto> BuildDocumentAsync(
        Report report, CancellationToken cancellationToken)
    {
        var parameters = ReadParameters(report);
        var today = _time.GetUtcNow().UtcDateTime.Date;
        var periodTo = report.PeriodTo?.Date ?? today;
        var periodFrom = report.PeriodFrom?.Date ?? periodTo.AddDays(-(ReportLimits.DefaultPeriodDays - 1));

        var builder = new DocumentBuilder(report, periodFrom, periodTo)
        {
            AppliedFilters = await DescribeFiltersAsync(parameters, report, cancellationToken)
        };

        switch (report.ReportType)
        {
            case ReportType.CaseSummary:
            case ReportType.InvestigationProgress:
                await ApplyCaseAsync(builder, report, cancellationToken);
                break;
            case ReportType.StatisticalAnalysis:
                await ApplyAnalyticsAsync(builder, parameters, cancellationToken);
                break;
            case ReportType.RiskAssessment:
                await ApplyRiskAsync(builder, parameters, cancellationToken);
                break;
            case ReportType.IntelligenceBrief:
                await ApplyExecutiveAsync(builder, parameters, cancellationToken);
                break;
            default:
                ApplyAuthored(builder);
                break;
        }

        return builder.Build();
    }

    private static void ApplyAuthored(DocumentBuilder builder)
    {
        if (!string.IsNullOrWhiteSpace(builder.Report.Description))
        {
            builder.Section("Summary").Rows(builder.Report.Description.Trim());
        }

        if (!string.IsNullOrWhiteSpace(builder.Report.Content))
        {
            builder.Section("Analyst observations").Rows(SplitLines(builder.Report.Content));
        }

        builder.HasData = builder.SectionBuilders.Any(s => !s.IsEmpty);
    }

    // ------------------------------------------------------------ case reports

    private async Task ApplyCaseAsync(
        DocumentBuilder builder, Report report, CancellationToken cancellationToken)
    {
        if (report.CrimeCaseId is not { } caseId)
        {
            builder.HasData = false;
            return;
        }

        var detail = await _cases.GetCaseDetailsAsync(caseId, cancellationToken);
        if (detail is null)
        {
            builder.HasData = false;
            return;
        }

        var isInvestigation = report.ReportType == ReportType.InvestigationProgress;

        builder.Section("Case classification").Fields(
            Field("Case number", detail.CaseNumber),
            Field("Crime type", detail.CrimeTypeName),
            Field("Incident date", Date(detail.IncidentDate)),
            Field("Reported date", detail.ReportedDate is { } reported ? Date(reported) : "Not recorded"),
            Field("District", detail.DistrictName ?? "Not recorded"),
            Field("Police station", detail.PoliceStationName ?? "Not recorded"),
            Field("Severity", detail.Severity),
            Field("Status", detail.Status),
            Field("Assigned investigator", detail.InvestigatorName ?? "Unassigned"));

        // Data minimisation: names, roles and association dates only. National IDs,
        // dates of birth, private addresses and per-person notes never appear.
        builder.Section("Persons of interest")
            .Table(["Name", "Role", "Associated"], detail.Persons
                .OrderBy(p => p.Role, StringComparer.Ordinal)
                .ThenBy(p => p.FullName, StringComparer.Ordinal)
                .Take(ReportLimits.MaxTableRows)
                .Select(p => Line(p.FullName, p.Role, Date(p.AssociatedAt))));

        builder.Section("Locations")
            .Table(["Name", "Role", "District"], detail.Locations
                .OrderBy(l => l.Name, StringComparer.Ordinal)
                .Take(ReportLimits.MaxTableRows)
                .Select(l => Line(l.Name, l.LocationRole ?? "Scene", l.DistrictName ?? "Not recorded")));

        builder.Section("Evidence summary")
            .Table(["Type", "Recorded statuses", "Items"], detail.Evidence
                .GroupBy(e => e.EvidenceType, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Take(ReportLimits.MaxTableRows)
                .Select(g => Line(
                    g.Key,
                    string.Join(", ", g.Select(e => e.Status).Distinct().Order(StringComparer.Ordinal)),
                    Count(g.Count()))));

        if (isInvestigation)
        {
            builder.Section("Timeline")
                .Table(["When", "Event", "Type"], detail.Timeline
                    .OrderBy(t => t.EventDateTime)
                    .Take(ReportLimits.MaxTableRows)
                    .Select(t => Line(DateTimeText(t.EventDateTime), t.Title, t.EventType)));

            // Private investigation notes stay private in exported documents.
            builder.Section("Investigation notes (non-private)")
                .Rows(detail.Notes
                    .Where(n => !n.IsPrivate)
                    .OrderBy(n => n.CreatedAt)
                    .Take(ReportLimits.MaxTableRows)
                    .Select(n => $"{Date(n.CreatedAt)} · {n.NoteType} · {n.AuthorName}: {n.Content}"));
        }
        else
        {
            builder.Section("Timeline summary")
                .Rows(detail.Timeline
                    .OrderBy(t => t.EventDateTime)
                    .Take(ReportLimits.MaxTableRows)
                    .Select(t => $"{DateTimeText(t.EventDateTime)} · {t.EventType} · {t.Title}"));
        }

        var linked = await GetLinkedCasesAsync(caseId, cancellationToken);
        builder.LinkedCases = linked;

        builder.Findings.Add($"Case {detail.CaseNumber} records {detail.Persons.Count} person association(s), " +
                             $"{detail.Locations.Count} location(s), {detail.Evidence.Count} evidence item(s) " +
                             $"and {detail.Timeline.Count} timeline event(s).");
        if (detail.InvestigatorName is null)
        {
            builder.Findings.Add("No investigator is currently assigned to this case.");
        }

        if (linked.Count > 0)
        {
            builder.Findings.Add($"{linked.Count} other case(s) in NETRA share one or more recorded " +
                                 "persons with this case. Association is recorded case data, not a " +
                                 "finding of involvement.");
        }

        builder.Metrics.Add(new ReportMetricDto("Persons", Count(detail.Persons.Count), "recorded associations"));
        builder.Metrics.Add(new ReportMetricDto("Locations", Count(detail.Locations.Count), "recorded scenes"));
        builder.Metrics.Add(new ReportMetricDto("Evidence", Count(detail.Evidence.Count), "items recorded"));
        builder.Metrics.Add(new ReportMetricDto("Timeline", Count(detail.Timeline.Count), "events recorded"));
        builder.HasData = true;
    }

    private async Task<List<LinkedCaseDto>> GetLinkedCasesAsync(
        Guid caseId, CancellationToken cancellationToken)
    {
        var people = await _context.CasePersons.AsNoTracking()
            .Where(cp => cp.CrimeCaseId == caseId)
            .Select(cp => new { cp.PersonId, Name = cp.Person.FirstName + " " + cp.Person.LastName })
            .Distinct()
            .ToListAsync(cancellationToken);

        if (people.Count == 0)
        {
            return [];
        }

        var personIds = people.Select(p => p.PersonId).ToList();
        var nameByPerson = people.GroupBy(p => p.PersonId).ToDictionary(g => g.Key, g => g.First().Name);

        var shared = await _context.CasePersons.AsNoTracking()
            .Where(cp => personIds.Contains(cp.PersonId) && cp.CrimeCaseId != caseId)
            .Select(cp => new
            {
                cp.CrimeCaseId,
                cp.CrimeCase.CaseNumber,
                cp.CrimeCase.Title,
                Status = cp.CrimeCase.Status.ToString(),
                cp.PersonId
            })
            .ToListAsync(cancellationToken);

        return shared
            .GroupBy(x => x.CrimeCaseId)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.First().CaseNumber, StringComparer.Ordinal)
            .Take(ReportLimits.MaxLinkedCases)
            .Select(g => new LinkedCaseDto(
                g.Key,
                g.First().CaseNumber,
                g.First().Title,
                $"{g.First().Status} · shared with: " +
                $"{string.Join(", ", g.Select(x => nameByPerson[x.PersonId]).Distinct().Order(StringComparer.Ordinal))}"))
            .ToList();
    }

    // -------------------------------------------------------- analytics report

    private async Task ApplyAnalyticsAsync(
        DocumentBuilder builder, ReportParameters parameters, CancellationToken cancellationToken)
    {
        var report = await _analytics.GetReportAsync(ToAnalyticsCriteria(parameters), cancellationToken);
        var total = report.TotalCrimeVolume;

        builder.Section("Volume overview").Fields(
            Field("Reporting period", $"{Date(report.DateFrom)} – {Date(report.DateTo)}"),
            Field("Total cases recorded", Count(total)),
            Field("High or critical severity", Count(report.HighSeverityCount)),
            Field("Average daily volume", report.AverageDailyVolume.ToString("0.0", CultureInfo.InvariantCulture)),
            Field("Clearance rate", report.ClearanceRate.ToString("0.0", CultureInfo.InvariantCulture) + "%"));

        builder.Section("Crime type breakdown")
            .Table(["Crime type", "Cases"], Top(report.CrimeByType).Select(c => Line(c.Label, Count(c.Count))));

        builder.Section("District comparison")
            .Table(["District", "Low", "Medium", "High", "Critical", "Total"], report.CrimeByDistrict
                .OrderByDescending(d => d.Total)
                .ThenBy(d => d.DistrictName, StringComparer.Ordinal)
                .Take(ReportLimits.MaxTableRows)
                .Select(d => Line(d.DistrictName, Count(d.Low), Count(d.Medium), Count(d.High),
                    Count(d.Critical), Count(d.Total))));

        builder.Section("Severity distribution")
            .Table(["Severity", "Cases"], report.SeverityDistribution
                .Select(s => Line(s.Label, Count(s.Count))));

        if (report.Highlights.LeadingCrimeType is { } leadingType)
        {
            builder.Findings.Add($"Based on the available NETRA records, {leadingType} is the most frequent " +
                                 $"crime type in the period with {report.Highlights.LeadingCrimeTypeCount} case(s).");
        }

        if (report.Highlights.HighestVolumeDistrict is { } leadingDistrict)
        {
            builder.Findings.Add($"{leadingDistrict} records the highest case volume in the period " +
                                 $"({report.Highlights.HighestVolumeDistrictCount} case(s)).");
        }

        if (report.Highlights.PeakMonth is { } peakMonth && report.Highlights.PeakMonthCount > 0)
        {
            builder.Findings.Add($"The busiest month in the period is {peakMonth} with " +
                                 $"{report.Highlights.PeakMonthCount} case(s).");
        }

        if (report.MonthlyTrend.Count >= 2)
        {
            var first = report.MonthlyTrend[0];
            var last = report.MonthlyTrend[^1];
            var direction = last.Count > first.Count ? "an observed increase"
                : last.Count < first.Count ? "an observed decrease"
                : "no net change";
            builder.Findings.Add($"Comparing the first month of the period ({first.Label}: {first.Count}) with " +
                                 $"the last ({last.Label}: {last.Count}) shows {direction}. This measures " +
                                 "recorded case volume and is not a forecast.");
        }

        if (total == 0)
        {
            builder.Findings.Add("No cases match the selected reporting period and filters.");
        }

        builder.Metrics.Add(new ReportMetricDto("Total cases", Count(total), "in the reporting period"));
        builder.Metrics.Add(new ReportMetricDto("High / critical", Count(report.HighSeverityCount), "severity recorded"));
        builder.Metrics.Add(new ReportMetricDto("Crime types", Count(report.CrimeByType.Count), "represented"));
        builder.Metrics.Add(new ReportMetricDto("Districts", Count(report.CrimeByDistrict.Count), "with recorded cases"));

        builder.Trend = report.MonthlyTrend.Select(t => new ReportTrendPointDto(t.Label, t.Count)).ToList();
        builder.CrimeByType = report.CrimeByType.Select(c => new ReportCategoryCountDto(c.Label, c.Count)).ToList();
        builder.Severity = report.SeverityDistribution.Select(s => new ReportCategoryCountDto(s.Label, s.Count)).ToList();
        builder.Districts = report.CrimeByDistrict
            .Select(d => new ReportDistrictSeverityDto(d.DistrictName, d.Low, d.Medium, d.High, d.Critical)).ToList();
        builder.HasData = total > 0;
    }

    // ----------------------------------------------------------- risk report

    private async Task ApplyRiskAsync(
        DocumentBuilder builder, ReportParameters parameters, CancellationToken cancellationToken)
    {
        var analysis = await _risk.GetAnalysisAsync(ToAnalyticsCriteria(parameters), cancellationToken);
        var indicators = analysis.Indicators
            .OrderByDescending(i => i.Score ?? 0)
            .ThenBy(i => i.DistrictName, StringComparer.Ordinal)
            .ToList();

        builder.Section("Assessment window").Fields(
            Field("Analysis window", $"{Date(analysis.DateFrom)} – {Date(analysis.DateTo)}"),
            Field("Comparison window", $"{Date(analysis.ComparisonFrom)} – {Date(analysis.ComparisonTo)}"),
            Field("Cases in the analysis window", Count(analysis.TotalCasesInWindow)),
            Field("Last recorded assessment run",
                analysis.LastRecordedRunAt is { } run ? DateTimeText(run) : "No persisted run recorded"));

        builder.Section("District indicators")
            .Table(["District", "Cases", "Baseline", "Change", "Score", "Level"], indicators
                .Take(ReportLimits.MaxTableRows)
                .Select(i => Line(
                    i.DistrictName,
                    Count(i.CurrentCount),
                    Count(i.BaselineCount),
                    i.ChangePercent is { } change
                        ? change.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "%"
                        : "n/a",
                    i.Score is { } score ? score.ToString("0", CultureInfo.InvariantCulture) : "n/a",
                    i.InsufficientData ? "Insufficient data" : i.LevelLabel)));

        builder.Section("Indicator explanations")
            .Rows(indicators
                .Where(i => !string.IsNullOrWhiteSpace(i.Explanation))
                .Take(ReportLimits.MaxTableRows)
                .Select(i => $"{i.DistrictName}: {i.Explanation}"));

        builder.Section("Detected anomalies")
            .Table(["District", "Type", "Severity", "Confidence"], analysis.Anomalies
                .Take(ReportLimits.MaxTableRows)
                .Select(a => Line(
                    a.DistrictName,
                    AnomalyTypes.Label(a.AnomalyType),
                    a.Severity,
                    (a.Confidence * 100m).ToString("0", CultureInfo.InvariantCulture) + "%")));

        builder.Section("Anomaly detail")
            .Rows(analysis.Anomalies.Take(ReportLimits.MaxTableRows).Select(a => a.Description));

        foreach (var indicator in indicators
                     .Where(i => !i.InsufficientData && i.Score is > 0)
                     .Take(ReportLimits.MaxTableRows))
        {
            builder.Findings.Add($"Analytical indicator — {indicator.DistrictName} scores " +
                                 $"{indicator.Score:0} of 100 ({indicator.LevelLabel}) on " +
                                 $"{indicator.CurrentCount} recorded case(s) against a baseline of " +
                                 $"{indicator.BaselineCount}.");
        }

        if (analysis.Anomalies.Count > 0)
        {
            builder.Findings.Add($"{analysis.Anomalies.Count} unusual pattern(s) were detected by comparing " +
                                 "the analysis window with its baseline.");
        }

        builder.Findings.Add("Risk values are analytical indicators derived from recorded case data. They " +
                             "are decision support for authorised personnel, not predictions of future crime.");

        builder.Metrics.Add(new ReportMetricDto("Cases in window", Count(analysis.TotalCasesInWindow), "recorded incidents"));
        builder.Metrics.Add(new ReportMetricDto("Districts scored",
            Count(indicators.Count(i => !i.InsufficientData)), "with sufficient data"));
        builder.Metrics.Add(new ReportMetricDto("High / critical areas",
            Count(indicators.Count(i => i.Level >= 4)), "level 4 or 5"));
        builder.Metrics.Add(new ReportMetricDto("Anomalies", Count(analysis.Anomalies.Count), "detected in window"));
        builder.HasData = analysis.TotalCasesInWindow > 0;
    }

    // --------------------------------------------------- executive intelligence

    private async Task ApplyExecutiveAsync(
        DocumentBuilder builder, ReportParameters parameters, CancellationToken cancellationToken)
    {
        var criteria = ToAnalyticsCriteria(parameters);
        var dashboard = await _dashboard.GetDashboardAsync(cancellationToken);
        var analytics = await _analytics.GetReportAsync(criteria, cancellationToken);
        var risk = await _risk.GetAnalysisAsync(criteria, cancellationToken);
        var alertSummary = await _alerts.GetSummaryAsync(cancellationToken);
        var alerts = await _alerts.ListAsync(
            new AlertFilterCriteria { Page = 1, PageSize = ReportLimits.MaxTableRows }, cancellationToken);

        builder.Section("Period position").Fields(
            Field("Reporting period", $"{Date(analytics.DateFrom)} – {Date(analytics.DateTo)}"),
            Field("Cases in period", Count(analytics.TotalCrimeVolume)),
            Field("Cases recorded overall", Count(dashboard.TotalCases)),
            Field("Active cases", Count(dashboard.ActiveCases)),
            Field("High-risk areas", Count(dashboard.HighRiskAreaCount)),
            Field("Active alerts", Count(alertSummary.OpenCount)));

        builder.Section("District comparison")
            .Table(["District", "Low", "Medium", "High", "Critical", "Total"], analytics.CrimeByDistrict
                .OrderByDescending(d => d.Total)
                .ThenBy(d => d.DistrictName, StringComparer.Ordinal)
                .Take(ReportLimits.MaxTableRows)
                .Select(d => Line(d.DistrictName, Count(d.Low), Count(d.Medium), Count(d.High),
                    Count(d.Critical), Count(d.Total))));

        builder.Section("Analytical indicators")
            .Table(["District", "Score", "Level"], risk.Indicators
                .Where(i => !i.InsufficientData)
                .OrderByDescending(i => i.Score ?? 0)
                .ThenBy(i => i.DistrictName, StringComparer.Ordinal)
                .Take(ReportLimits.MaxTableRows)
                .Select(i => Line(i.DistrictName,
                    i.Score?.ToString("0", CultureInfo.InvariantCulture) ?? "n/a", i.LevelLabel)));

        builder.Section("Notable anomalies")
            .Rows(risk.Anomalies.Take(ReportLimits.MaxTableRows).Select(a => a.Description));

        builder.Section("Active and high-severity alerts")
            .Table(["Severity", "Status", "Alert", "Raised"], alerts.Items
                .Where(a => a.Status != AlertStatus.Resolved && a.Status != AlertStatus.Dismissed)
                .Take(ReportLimits.MaxTableRows)
                .Select(a => Line(a.Severity.ToString(), a.Status.ToString(), a.Title, Date(a.CreatedAt))));

        builder.Section("Recent alerts of record")
            .Rows(dashboard.RecentAlerts
                .Take(ReportLimits.MaxTableRows)
                .Select(a => $"{Date(a.CreatedAt)} · {a.Severity} · {a.Title}"));

        if (analytics.Highlights.LeadingCrimeType is { } leadingType)
        {
            builder.Findings.Add($"Based on the available NETRA records, {leadingType} leads the period with " +
                                 $"{analytics.Highlights.LeadingCrimeTypeCount} case(s).");
        }

        if (analytics.Highlights.HighestVolumeDistrict is { } topDistrict)
        {
            builder.Findings.Add($"{topDistrict} carries the highest recorded volume in the period " +
                                 $"({analytics.Highlights.HighestVolumeDistrictCount} case(s)).");
        }

        var highLevelAreas = risk.Indicators.Count(i => i.Level >= 4);
        builder.Findings.Add(highLevelAreas > 0
            ? $"{highLevelAreas} district(s) currently score as high or critical analytical indicators; " +
              "review by authorised personnel is recommended."
            : "No district currently scores as a high or critical analytical indicator.");

        builder.Findings.Add(alertSummary.CriticalCount + alertSummary.HighCount > 0
            ? $"{alertSummary.CriticalCount} critical and {alertSummary.HighCount} high-severity alerts " +
              "are open in the alert feed."
            : "No critical or high-severity alerts are open in the alert feed.");

        builder.Findings.Add("This summary supports executive decision making. It reports recorded data " +
                             "and analytical indicators; it does not direct operational action.");

        builder.Metrics.Add(new ReportMetricDto("Cases in period", Count(analytics.TotalCrimeVolume), "recorded incidents"));
        builder.Metrics.Add(new ReportMetricDto("Active cases", Count(dashboard.ActiveCases), "open investigations"));
        builder.Metrics.Add(new ReportMetricDto("Active alerts", Count(alertSummary.OpenCount), "awaiting triage"));
        builder.Metrics.Add(new ReportMetricDto("Anomalies", Count(risk.Anomalies.Count), "detected in window"));

        builder.Trend = analytics.MonthlyTrend.Select(t => new ReportTrendPointDto(t.Label, t.Count)).ToList();
        builder.CrimeByType = analytics.CrimeByType.Select(c => new ReportCategoryCountDto(c.Label, c.Count)).ToList();
        builder.Severity = analytics.SeverityDistribution.Select(s => new ReportCategoryCountDto(s.Label, s.Count)).ToList();
        builder.Districts = analytics.CrimeByDistrict
            .Select(d => new ReportDistrictSeverityDto(d.DistrictName, d.Low, d.Medium, d.High, d.Critical)).ToList();
        builder.HasData = analytics.TotalCrimeVolume > 0 || alertSummary.OpenCount > 0;
    }

    // -------------------------------------------------------------- generation

    public async Task<OperationResult<Guid>> GenerateAsync(
        GenerateReportRequest request, Guid actorUserId, bool allowAnalytical,
        CancellationToken cancellationToken = default)
    {
        if (!ReportTypes.GeneratedOnly.Contains(request.Type))
        {
            return OperationResult<Guid>.ValidationFailed(new Dictionary<string, string[]>
            {
                [nameof(request.Type)] =
                [
                    $"{ReportTypes.Label(request.Type)} cannot be generated. Author it from the report editor instead."
                ]
            });
        }

        if (ReportTypes.IsAnalytical(request.Type) && !allowAnalytical)
        {
            return OperationResult<Guid>.Forbidden(
                "Analytical reports are restricted to authorised analytical roles.");
        }

        var errors = new Dictionary<string, string[]>();

        if (ReportTypes.CaseScoped.Contains(request.Type))
        {
            if (request.CaseId is null)
            {
                errors[nameof(request.CaseId)] = ["Select the case this report summarises."];
            }
            else if (!await _context.CrimeCases.AnyAsync(c => c.Id == request.CaseId, cancellationToken))
            {
                errors[nameof(request.CaseId)] = ["The selected case was not found."];
            }
        }

        if (!string.IsNullOrWhiteSpace(request.AnalystObservations)
            && request.AnalystObservations.Trim().Length > ReportLimits.MaxContentLength)
        {
            errors[nameof(request.AnalystObservations)] =
                [$"Analyst observations must be {ReportLimits.MaxContentLength} characters or fewer."];
        }

        if (errors.Count > 0)
        {
            return OperationResult<Guid>.ValidationFailed(errors);
        }

        var (periodFrom, periodTo) = ResolvePeriod(request.DateFrom, request.DateTo, _time.GetUtcNow().UtcDateTime);
        var observations = Truncate(Trim(request.AnalystObservations), ReportLimits.MaxContentLength);

        var report = new Report
        {
            Title = await BuildTitleAsync(request.Type, request.CaseId, periodFrom, periodTo, cancellationToken),
            ReportType = request.Type,
            Source = ReportSource.SystemGenerated,
            Status = ReportStatus.Ready,
            CrimeCaseId = ReportTypes.CaseScoped.Contains(request.Type) ? request.CaseId : null,
            PeriodFrom = periodFrom,
            PeriodTo = periodTo,
            Content = observations.Length == 0 ? null : observations,
            // Only the parameters are stored, never the results — and observations are a result,
            // already held by Content. Repeating them here would put up to 8000 characters into
            // the 4000-character Parameters column, which SQL Server rejects with error 2628
            // while InMemory silently allows it.
            Parameters = JsonSerializer.Serialize(new ReportParameters(
                request.CaseId, request.DistrictId, request.CrimeTypeId,
                NormalizeSeverity(request.Severity), periodFrom, periodTo), JsonOptions),
            RequestedById = actorUserId,
            GeneratedById = actorUserId,
            GeneratedAt = _time.GetUtcNow().UtcDateTime,
            RequestedAt = _time.GetUtcNow().UtcDateTime,
            CreatedAt = _time.GetUtcNow().UtcDateTime
        };

        // The stored summary is produced from the same live reads the document view
        // performs, so the list can never contradict the report it links to.
        var preview = await BuildDocumentAsync(report, cancellationToken);
        report.Description = Truncate(BuildSummary(request.Type, preview), ReportLimits.MaxSummaryLength);
        report.GeneratedFilePath =
            preview.Sections.Count(section => !section.IsEmpty).ToString(CultureInfo.InvariantCulture);

        _context.Reports.Add(report);
        _audit.Record(
            AuditEntityTypeNames.Report, report.Id, AuditActionNames.ReportGenerated, actorUserId,
            newValue: Truncate($"{ReportTypes.Label(report.ReportType)} · {report.Title}", 200));
        await _context.SaveChangesAsync(cancellationToken);

        return OperationResult<Guid>.Success(report.Id);
    }

    public async Task<OperationResult<Guid>> CreateAsync(
        CreateAnalystReportRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var errors = Validate(request.Title, request.Summary, request.Content,
            request.PeriodFrom, request.PeriodTo);

        if (!ReportTypes.Authorable.Contains(request.Type))
        {
            errors[nameof(request.Type)] =
                [$"{ReportTypes.Label(request.Type)} is produced by generation, not authored by hand."];
        }

        if (request.CaseId is { } caseId
            && !await _context.CrimeCases.AnyAsync(c => c.Id == caseId, cancellationToken))
        {
            errors[nameof(request.CaseId)] = ["The selected case was not found."];
        }

        if (errors.Count > 0)
        {
            return OperationResult<Guid>.ValidationFailed(errors);
        }

        var (periodFrom, periodTo) = ResolvePeriod(request.PeriodFrom, request.PeriodTo, _time.GetUtcNow().UtcDateTime);

        var report = new Report
        {
            Title = request.Title.Trim(),
            ReportType = request.Type,
            Source = ReportSource.AnalystAuthored,
            Status = ReportStatus.Draft,
            Description = Truncate(Trim(request.Summary), ReportLimits.MaxSummaryLength),
            Content = Truncate(Trim(request.Content), ReportLimits.MaxContentLength),
            CrimeCaseId = request.CaseId,
            PeriodFrom = periodFrom,
            PeriodTo = periodTo,
            RequestedById = actorUserId,
            RequestedAt = _time.GetUtcNow().UtcDateTime,
            CreatedAt = _time.GetUtcNow().UtcDateTime
        };

        _context.Reports.Add(report);
        _audit.Record(
            AuditEntityTypeNames.Report, report.Id, AuditActionNames.ReportCreated, actorUserId,
            newValue: Truncate($"{ReportTypes.Label(report.ReportType)} · {report.Title}", 200));
        await _context.SaveChangesAsync(cancellationToken);

        return OperationResult<Guid>.Success(report.Id);
    }

    public async Task<OperationResult> EditAsync(
        Guid reportId, EditAnalystReportRequest request, Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var report = await _context.Reports.FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken);
        if (report is null)
        {
            return OperationResult.NotFound("The report was not found.");
        }

        if (report.Source == ReportSource.SystemGenerated)
        {
            return OperationResult.Conflict(
                nameof(report.Source),
                "System-generated facts are immutable. Record your observations on the report instead.");
        }

        if (report.Status == ReportStatus.Archived)
        {
            return OperationResult.Conflict(nameof(report.Status), "Archived reports cannot be edited.");
        }

        var errors = Validate(request.Title, request.Summary, request.Content,
            request.PeriodFrom, request.PeriodTo);
        if (errors.Count > 0)
        {
            return OperationResult.ValidationFailed(errors);
        }

        var (periodFrom, periodTo) = ResolvePeriod(request.PeriodFrom, request.PeriodTo, _time.GetUtcNow().UtcDateTime);

        report.Title = request.Title.Trim();
        report.Description = Truncate(Trim(request.Summary), ReportLimits.MaxSummaryLength);
        report.Content = Truncate(Trim(request.Content), ReportLimits.MaxContentLength);
        report.PeriodFrom = periodFrom;
        report.PeriodTo = periodTo;
        report.Status = ReportStatus.Ready;

        _audit.Record(
            AuditEntityTypeNames.Report, report.Id, AuditActionNames.ReportEdited, actorUserId,
            propertyName: nameof(Report.Title), newValue: Truncate(report.Title, 200));
        await _context.SaveChangesAsync(cancellationToken);
        return OperationResult.Success();
    }

    public async Task<OperationResult> ArchiveAsync(
        Guid reportId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var report = await _context.Reports.FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken);
        if (report is null)
        {
            return OperationResult.NotFound("The report was not found.");
        }

        if (report.Status == ReportStatus.Archived)
        {
            return OperationResult.Conflict(nameof(report.Status), "This report is already archived.");
        }

        var previous = report.Status;
        report.Status = ReportStatus.Archived;
        report.ArchivedAt = _time.GetUtcNow().UtcDateTime;

        _audit.Record(
            AuditEntityTypeNames.Report, report.Id, AuditActionNames.ReportArchived, actorUserId,
            propertyName: nameof(Report.Status),
            oldValue: ReportLabels.Status(previous),
            newValue: ReportLabels.Status(ReportStatus.Archived));
        await _context.SaveChangesAsync(cancellationToken);
        return OperationResult.Success();
    }

    // ------------------------------------------------------------------ export

    public async Task<(byte[] Content, string FileName)?> ExportCsvAsync(
        Guid reportId, Guid actorUserId, bool allowAnalytical, CancellationToken cancellationToken = default)
    {
        var document = await GetDocumentAsync(reportId, actorUserId, allowAnalytical, cancellationToken);
        if (document is null)
        {
            return null;
        }

        var builder = new StringBuilder();

        Row("NETRA report export");
        Row("Title", document.Title);
        Row("Report type", ReportTypes.Label(document.Type));
        Row("Source", ReportLabels.Source(document.Source));
        Row("Status", ReportLabels.Status(document.Status));
        Row("Reporting period", $"{Date(document.PeriodFrom)} – {Date(document.PeriodTo)}");
        Row("Created", document.CreatedAt.ToString("u", CultureInfo.InvariantCulture));
        Row("Generated", document.GeneratedAt?.ToString("u", CultureInfo.InvariantCulture) ?? "Not system generated");
        Row("Author", document.AuthorName ?? "Unknown");
        Row("Filters", string.Join("; ", document.AppliedFilters));
        builder.AppendLine();

        if (document.Metrics.Count > 0)
        {
            Row("Metric", "Value", "Note");
            foreach (var metric in document.Metrics)
            {
                Row(metric.Label, metric.Value, metric.Meta);
            }

            builder.AppendLine();
        }

        if (document.Findings.Count > 0)
        {
            Row("Analytical observation");
            foreach (var finding in document.Findings)
            {
                Row(finding);
            }

            builder.AppendLine();
        }

        foreach (var section in document.Sections.Where(s => !s.IsEmpty))
        {
            Row(section.Heading);
            if (section.Table.Count > 0)
            {
                Row(section.Columns.ToArray());
                foreach (var line in section.Table)
                {
                    Row(line.ToArray());
                }
            }
            else if (section.Fields.Count > 0)
            {
                Row("Field", "Value");
                foreach (var field in section.Fields)
                {
                    Row(field.Label, field.Value);
                }
            }
            else
            {
                foreach (var line in section.Rows)
                {
                    Row(line);
                }
            }

            builder.AppendLine();
        }

        if (document.LinkedCases.Count > 0)
        {
            Row("Linked case", "Case number", "Status and shared persons");
            foreach (var linked in document.LinkedCases)
            {
                Row(linked.Title, linked.CaseNumber, linked.Status);
            }

            builder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(document.AnalystContent))
        {
            Row("Analyst observations");
            foreach (var line in SplitLines(document.AnalystContent))
            {
                Row(line);
            }

            builder.AppendLine();
        }

        Row("Disclaimer",
            "This report summarises recorded NETRA data and analytical indicators. It is decision support " +
            "for authorised personnel, not a finding of wrongdoing and not a prediction.");

        var fileName = $"{Slug(document.Title)}-{document.Id:N}".ToLowerInvariant() + ".csv";

        _audit.Record(
            AuditEntityTypeNames.Report, reportId, AuditActionNames.ReportExported, actorUserId,
            newValue: Truncate(fileName, 200));
        await _context.SaveChangesAsync(cancellationToken);

        return (Encoding.UTF8.GetBytes(builder.ToString()), fileName);

        void Row(params string?[] values) =>
            builder.AppendLine(string.Join(',', values.Select(EscapeCsvField)));
    }

    // ----------------------------------------------------------------- helpers

    private sealed record ReportListRow(
        Guid Id, string Title, ReportType Type, ReportSource Source, ReportStatus Status,
        string? Summary, DateTime? PeriodFrom, DateTime? PeriodTo, DateTime CreatedAt,
        DateTime? GeneratedAt, string AuthorName, string? CaseNumber, string? SectionCount);

    private sealed record ReportParameters(
        Guid? CaseId,
        Guid? DistrictId,
        Guid? CrimeTypeId,
        string? Severity,
        DateTime? DateFrom,
        DateTime? DateTo);

    /// <summary>Mutable staging object for a report document under construction.</summary>
    private sealed class DocumentBuilder(Report report, DateTime periodFrom, DateTime periodTo)
    {
        public Report Report { get; } = report;

        public List<SectionBuilder> SectionBuilders { get; } = [];

        public List<string> Findings { get; } = [];

        public List<ReportMetricDto> Metrics { get; } = [];

        public List<LinkedCaseDto> LinkedCases { get; set; } = [];

        public List<ReportTrendPointDto> Trend { get; set; } = [];

        public List<ReportCategoryCountDto> CrimeByType { get; set; } = [];

        public List<ReportCategoryCountDto> Severity { get; set; } = [];

        public List<ReportDistrictSeverityDto> Districts { get; set; } = [];

        public IReadOnlyList<string> AppliedFilters { get; init; } = Array.Empty<string>();

        public bool HasData { get; set; }

        public SectionBuilder Section(string heading)
        {
            var section = new SectionBuilder(heading);
            SectionBuilders.Add(section);
            return section;
        }

        public ReportDocumentDto Build() => new()
        {
            Id = Report.Id,
            Title = Report.Title,
            Type = Report.ReportType,
            Source = Report.Source,
            Status = Report.Status,
            CreatedAt = Report.CreatedAt,
            GeneratedAt = Report.GeneratedAt,
            ArchivedAt = Report.ArchivedAt,
            PeriodFrom = periodFrom,
            PeriodTo = periodTo,
            AuthorName = Report.RequestedBy is null
                ? null
                : $"{Report.RequestedBy.FirstName} {Report.RequestedBy.LastName}".Trim(),
            GeneratedByName = Report.GeneratedBy is null
                ? null
                : $"{Report.GeneratedBy.FirstName} {Report.GeneratedBy.LastName}".Trim(),
            Summary = Report.Description,
            AnalystContent = Report.Content,
            CaseId = Report.CrimeCaseId,
            CaseNumber = Report.CrimeCase?.CaseNumber,
            AppliedFilters = AppliedFilters,
            Sections = SectionBuilders.Select(s => s.Build()).ToList(),
            Findings = Findings,
            Metrics = Metrics,
            LinkedCases = LinkedCases,
            Trend = Trend,
            CrimeByType = CrimeByType,
            SeverityDistribution = Severity,
            CrimeByDistrict = Districts,
            HasData = HasData
        };
    }

    private sealed class SectionBuilder(string heading)
    {
        public string Heading { get; } = heading;

        public List<string> RowLines { get; } = [];

        public List<ReportFieldDto> FieldLines { get; } = [];

        public List<string> ColumnNames { get; } = [];

        public List<IReadOnlyList<string>> TableLines { get; } = [];

        public bool IsEmpty => RowLines.Count == 0 && FieldLines.Count == 0 && TableLines.Count == 0;

        public SectionBuilder Rows(IEnumerable<string> rows)
        {
            RowLines.AddRange(rows.Where(r => !string.IsNullOrWhiteSpace(r)));
            return this;
        }

        public SectionBuilder Rows(string row) => Rows([row]);

        public SectionBuilder Fields(params ReportFieldDto[] fields)
        {
            FieldLines.AddRange(fields);
            return this;
        }

        public SectionBuilder Table(string[] columns, IEnumerable<IEnumerable<string>> rows)
        {
            ColumnNames.AddRange(columns);
            TableLines.AddRange(rows.Select(r => (IReadOnlyList<string>)r.ToList()));
            return this;
        }

        public ReportSectionDto Build() => new()
        {
            Heading = Heading,
            Rows = RowLines,
            Fields = FieldLines,
            Columns = ColumnNames,
            Table = TableLines
        };
    }

    private static ReportFieldDto Field(string label, string value) => new(label, value);

    private static IEnumerable<string> Line(params string[] values) => values;

    /// <summary>CaseQueryFilters parses this string with Enum.TryParse, so blank means "no filter".</summary>
    private static string? NormalizeSeverity(string? severity) =>
        string.IsNullOrWhiteSpace(severity) ? null : severity.Trim();

    private static AnalyticsFilterCriteria ToAnalyticsCriteria(ReportParameters parameters) => new()
    {
        DateFrom = parameters.DateFrom,
        DateTo = parameters.DateTo,
        DistrictId = parameters.DistrictId,
        CrimeTypeId = parameters.CrimeTypeId,
        Severity = parameters.Severity
    };

    private static ReportParameters ReadParameters(Report report)
    {
        if (string.IsNullOrWhiteSpace(report.Parameters))
        {
            return EmptyParameters;
        }

        try
        {
            return JsonSerializer.Deserialize<ReportParameters>(report.Parameters, JsonOptions) ?? EmptyParameters;
        }
        catch (JsonException)
        {
            // A parameter blob written by another tool must never break the list or
            // the document view; the report still renders from its stored fields.
            return EmptyParameters;
        }
    }

    private static readonly ReportParameters EmptyParameters =
        new(null, null, null, null, null, null);

    private async Task<List<string>> DescribeFiltersAsync(
        ReportParameters parameters, Report report, CancellationToken cancellationToken)
    {
        var labels = new List<string>();

        if (report.CrimeCase?.CaseNumber is { } caseNumber)
        {
            labels.Add($"Case {caseNumber}");
        }

        if (parameters.DistrictId is { } districtId)
        {
            var name = await _context.Districts.AsNoTracking()
                .Where(d => d.Id == districtId).Select(d => d.Name).FirstOrDefaultAsync(cancellationToken);
            if (name is not null)
            {
                labels.Add($"District {name}");
            }
        }

        if (parameters.CrimeTypeId is { } crimeTypeId)
        {
            var name = await _context.CrimeTypes.AsNoTracking()
                .Where(c => c.Id == crimeTypeId).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken);
            if (name is not null)
            {
                labels.Add($"Crime type {name}");
            }
        }

        if (!string.IsNullOrWhiteSpace(parameters.Severity))
        {
            labels.Add($"Severity {parameters.Severity}");
        }

        if (report.PeriodFrom is { } from && report.PeriodTo is { } to)
        {
            labels.Add($"Period {Date(from)} – {Date(to)}");
        }

        return labels;
    }

    private async Task<string> BuildTitleAsync(
        ReportType type, Guid? caseId, DateTime periodFrom, DateTime periodTo,
        CancellationToken cancellationToken)
    {
        var label = ReportTypes.Label(type);
        if (ReportTypes.CaseScoped.Contains(type) && caseId is { } id)
        {
            var number = await _context.CrimeCases.AsNoTracking()
                .Where(c => c.Id == id).Select(c => c.CaseNumber).FirstOrDefaultAsync(cancellationToken);
            return $"{label} — {number ?? "case"}";
        }

        return $"{label} — {Date(periodFrom)} to {Date(periodTo)}";
    }

    private static string BuildSummary(ReportType type, ReportDocumentDto document)
    {
        if (!document.HasData)
        {
            return type switch
            {
                ReportType.StatisticalAnalysis or ReportType.RiskAssessment or ReportType.IntelligenceBrief =>
                    "No records matched the selected reporting period and filters, so this report contains no findings.",
                _ => "The source records for this report were not available when it was produced."
            };
        }

        var headline = document.Metrics.Count > 0
            ? $"{document.Metrics[0].Label}: {document.Metrics[0].Value} {document.Metrics[0].Meta}".Trim()
            : $"{document.Sections.Count(s => !s.IsEmpty)} section(s) of recorded detail";

        var observation = document.Findings.FirstOrDefault()
            ?? "See the report body for the recorded detail behind this summary.";

        return $"{headline}. {observation}";
    }

    private static Dictionary<string, string[]> Validate(
        string? title, string? summary, string? content, DateTime? periodFrom, DateTime? periodTo)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length < ReportLimits.MinTitleLength)
        {
            errors[nameof(title)] =
                [$"Title must be between {ReportLimits.MinTitleLength} and {ReportLimits.MaxTitleLength} characters."];
        }
        else if (title.Trim().Length > ReportLimits.MaxTitleLength)
        {
            errors[nameof(title)] = [$"Title must be {ReportLimits.MaxTitleLength} characters or fewer."];
        }

        if (summary is { Length: > ReportLimits.MaxSummaryLength })
        {
            errors[nameof(summary)] = [$"Summary must be {ReportLimits.MaxSummaryLength} characters or fewer."];
        }

        if (content is { Length: > ReportLimits.MaxContentLength })
        {
            errors[nameof(content)] = [$"Observations must be {ReportLimits.MaxContentLength} characters or fewer."];
        }

        if (periodFrom is { } from && periodTo is { } to && from.Date > to.Date)
        {
            errors[nameof(periodFrom)] = ["The reporting period start must be on or before its end."];
        }

        return errors;
    }

    private static (DateTime From, DateTime To) ResolvePeriod(
        DateTime? dateFrom, DateTime? dateTo, DateTime nowUtc)
    {
        var to = (dateTo ?? nowUtc).Date;
        var from = (dateFrom ?? to.AddDays(-(ReportLimits.DefaultPeriodDays - 1))).Date;

        if (from > to)
        {
            (from, to) = (to, from);
        }

        if ((to - from).TotalDays > ReportLimits.MaxPeriodDays)
        {
            from = to.AddDays(-ReportLimits.MaxPeriodDays);
        }

        return (from, to);
    }

    private static IEnumerable<ReportCategoryCountDto> Top(IReadOnlyList<CategoryCountDto> rows) =>
        rows.OrderByDescending(r => r.Count)
            .ThenBy(r => r.Label, StringComparer.Ordinal)
            .Take(ReportLimits.MaxTableRows)
            .Select(r => new ReportCategoryCountDto(r.Label, r.Count));

    private static List<string> SplitLines(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

    private static string Trim(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string Date(DateTime value) => value.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    private static string DateTimeText(DateTime value) =>
        value.ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture);

    private static string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty
        : value.Length <= max ? value : value[..max];

    private static int ParseSectionCount(string? stored) =>
        int.TryParse(stored, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ? count : 0;

    private static string Slug(string value)
    {
        var slug = new string([.. value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).Take(48)]);
        return slug.Length > 0 ? slug : "report";
    }

    private static string EscapeCsvField(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var escaped = value.Replace("\"", "\"\"").Replace('\r', ' ').Replace('\n', ' ');

        // Authored text can start with a formula marker; neutralise it so the export
        // cannot execute in a spreadsheet.
        if (escaped.Length > 0 && (escaped[0] == '=' || escaped[0] == '+' || escaped[0] == '-' || escaped[0] == '@'))
        {
            escaped = "'" + escaped;
        }

        return escaped.Contains(',') || escaped.Contains('"')
            ? $"\"{escaped}\""
            : escaped;
    }
}
