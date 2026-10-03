using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using NETRA.Application.Abstractions;
using NETRA.Application.Alerts;
using NETRA.Application.Analytics;
using NETRA.Application.Risk;
using NETRA.Domain.Alerts;
using NETRA.Domain.Enums;
using NETRA.Domain.People;
using NETRA.Infrastructure.Data;

namespace NETRA.Infrastructure.Services;

/// <summary>
/// Shared predicate for "open" (not yet closed) alerts. Used by the alert
/// summary and by the dashboard, so alert-count logic exists exactly once.
/// </summary>
public static class AlertQueryFilters
{
    public static readonly Expression<Func<Alert, bool>> IsActive =
        a => a.Status != AlertStatus.Resolved && a.Status != AlertStatus.Dismissed;
}

/// <summary>
/// Alert reading, lifecycle and deterministic generation. Generation orchestrates
/// the existing analytical subsystems (<see cref="IRiskAnalysisService"/>, anomaly
/// results, recorded case associations and the audit trail) — it never invents
/// conditions and never duplicates: every generated alert carries a stable
/// <c>AlertKey</c> (category + source + analysis window) guarded by a check
/// before insert and a unique filtered index in the database.
/// </summary>
public sealed class AlertService : IAlertService
{
    /// <summary>Role names mirroring <c>NetraRoles</c> (Web) for recipient lookup.</summary>
    public static class RecipientRoles
    {
        public const string Administrator = "System Administrator";
        public const string SeniorOfficer = "Senior Officer";
        public const string Analyst = "Police / Intelligence Analyst";
        public const string Investigator = "Investigator";
    }

    private const int SecuritySignInFailureThreshold = 5;

    private readonly NetraDbContext _context;
    private readonly IRiskAnalysisService _risk;
    private readonly IAuditService _audit;
    private readonly TimeProvider _time;

    public AlertService(
        NetraDbContext context,
        IRiskAnalysisService risk,
        IAuditService audit,
        TimeProvider time)
    {
        _context = context;
        _risk = risk;
        _audit = audit;
        _time = time;
    }

    public async Task<PagedAlertsDto> ListAsync(
        AlertFilterCriteria criteria, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, criteria.Page);
        var pageSize = Math.Clamp(criteria.PageSize, 1, AlertLimits.MaxPageSize);

        var query = _context.Alerts.AsNoTracking();

        if (criteria.Severity is { } severity)
        {
            query = query.Where(a => a.Severity == severity);
        }

        if (criteria.Status is { } status)
        {
            query = query.Where(a => a.Status == status);
        }

        if (criteria.Category is { } category)
        {
            var categoryValue = (int)category;
            query = query.Where(a => a.AlertType == categoryValue);
        }

        if (criteria.DateFrom is { } dateFrom)
        {
            query = query.Where(a => a.CreatedAt >= dateFrom);
        }

        if (criteria.DateTo is { } dateTo)
        {
            // Inclusive of the whole final day.
            var endExclusive = dateTo.Date.AddDays(1);
            query = query.Where(a => a.CreatedAt < endExclusive);
        }

        if (criteria.CaseId is { } caseId)
        {
            query = query.Where(a => a.CrimeCaseId == caseId);
        }

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(a => a.CreatedAt)
            // Same-batch alerts share a timestamp; SQL Server orders ties arbitrarily.
            .ThenBy(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AlertListItemDto
            {
                Id = a.Id,
                Title = a.Title,
                Description = a.Description,
                Severity = a.Severity,
                Status = a.Status,
                Category = (AlertCategory)a.AlertType,
                CreatedAt = a.CreatedAt,
                AcknowledgedAt = a.AcknowledgedAt,
                ResolvedAt = a.ResolvedAt,
                CrimeCaseId = a.CrimeCaseId,
                CaseNumber = a.CrimeCase == null ? null : a.CrimeCase.CaseNumber,
                RelatedEntityLabel = a.RelatedEntityType == "District"
                    ? _context.Districts.Where(d => d.Id == a.RelatedEntityId).Select(d => d.Name).FirstOrDefault()
                    : a.RelatedEntityType == "Person"
                    ? _context.Persons.Where(p => p.Id == a.RelatedEntityId)
                        .Select(p => p.FirstName + " " + p.LastName).FirstOrDefault()
                    : null,
                IsGenerated = a.AlertKey != null
            })
            .ToListAsync(cancellationToken);

        return new PagedAlertsDto
        {
            Items = rows,
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        };
    }

    public async Task<IReadOnlyList<CaseOptionDto>> GetCaseOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        var cases = await _context.CrimeCases
            .AsNoTracking()
            .OrderByDescending(c => c.ReportedDate ?? c.CreatedAt)
            .Take(200)
            .Select(c => new { c.Id, c.CaseNumber })
            .ToListAsync(cancellationToken);

        return cases.Select(c => new CaseOptionDto(c.Id, c.CaseNumber)).ToList();
    }

    public async Task<AlertSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var active = _context.Alerts.AsNoTracking().Where(AlertQueryFilters.IsActive);

        return new AlertSummaryDto
        {
            CriticalCount = await active.CountAsync(a => a.Severity == AlertSeverity.Critical, cancellationToken),
            HighCount = await active.CountAsync(a => a.Severity == AlertSeverity.High, cancellationToken),
            OpenCount = await active.CountAsync(cancellationToken),
            TotalCount = await _context.Alerts.AsNoTracking().CountAsync(cancellationToken)
        };
    }

    public async Task<AlertDetailDto?> GetDetailAsync(
        Guid alertId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var detail = await _context.Alerts.AsNoTracking()
            .Where(a => a.Id == alertId)
            .Select(a => new AlertDetailDto
            {
                Id = a.Id,
                Title = a.Title,
                Description = a.Description,
                Severity = a.Severity,
                Status = a.Status,
                Category = (AlertCategory)a.AlertType,
                CreatedAt = a.CreatedAt,
                AcknowledgedAt = a.AcknowledgedAt,
                ResolvedAt = a.ResolvedAt,
                ResolvedByName = a.ResolvedBy == null
                    ? null
                    : a.ResolvedBy.FirstName + " " + a.ResolvedBy.LastName,
                ResolutionNotes = a.ResolutionNotes,
                CrimeCaseId = a.CrimeCaseId,
                CaseNumber = a.CrimeCase == null ? null : a.CrimeCase.CaseNumber,
                CaseTitle = a.CrimeCase == null ? null : a.CrimeCase.Title,
                RelatedEntityLabel = a.RelatedEntityType == "District"
                    ? _context.Districts.Where(d => d.Id == a.RelatedEntityId).Select(d => d.Name).FirstOrDefault()
                    : a.RelatedEntityType == "Person"
                    ? _context.Persons.Where(p => p.Id == a.RelatedEntityId)
                        .Select(p => p.FirstName + " " + p.LastName).FirstOrDefault()
                    : null,
                WindowStart = a.WindowStart,
                WindowEnd = a.WindowEnd,
                IsGenerated = a.AlertKey != null
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (detail is not null)
        {
            _audit.Record(
                AuditEntityTypeNames.Alert, alertId,
                AuditActionNames.AlertViewed, actorUserId,
                newValue: Truncate(detail.Title, 200));
            await _context.SaveChangesAsync(cancellationToken);
        }

        return detail;
    }

    public Task<OperationResult> AcknowledgeAsync(
        Guid alertId, Guid actorUserId, CancellationToken cancellationToken = default) =>
        TransitionAsync(alertId, actorUserId, AlertStatus.Acknowledged, null,
            AuditActionNames.AlertAcknowledged, cancellationToken);

    public Task<OperationResult> ResolveAsync(
        Guid alertId, string? resolutionNotes, Guid actorUserId, CancellationToken cancellationToken = default) =>
        TransitionAsync(alertId, actorUserId, AlertStatus.Resolved, resolutionNotes,
            AuditActionNames.AlertResolved, cancellationToken);

    public Task<OperationResult> DismissAsync(
        Guid alertId, string? reason, Guid actorUserId, CancellationToken cancellationToken = default) =>
        TransitionAsync(alertId, actorUserId, AlertStatus.Dismissed, reason,
            AuditActionNames.AlertDismissed, cancellationToken);

    private async Task<OperationResult> TransitionAsync(
        Guid alertId, Guid actorUserId, AlertStatus target, string? notes,
        string auditAction, CancellationToken cancellationToken)
    {
        var alert = await _context.Alerts
            .FirstOrDefaultAsync(a => a.Id == alertId, cancellationToken);

        if (alert is null)
        {
            return OperationResult.NotFound("The alert was not found.");
        }

        if (alert.Status == AlertStatus.Resolved || alert.Status == AlertStatus.Dismissed)
        {
            return OperationResult.Conflict(
                nameof(alert.Status),
                $"This alert is already {alert.Status.ToString().ToLowerInvariant()}.");
        }

        if (alert.Status == target)
        {
            return OperationResult.Conflict(
                nameof(alert.Status), $"This alert is already {target.ToString().ToLowerInvariant()}.");
        }

        var previous = alert.Status;
        var now = _time.GetUtcNow().UtcDateTime;
        alert.Status = target;

        if (target == AlertStatus.Acknowledged)
        {
            alert.AcknowledgedAt = now;
        }
        else
        {
            // Resolved and Dismissed are both closings; the model records the
            // closing moment and actor in the resolution fields.
            alert.ResolvedAt = now;
            alert.ResolvedById = actorUserId;
            if (!string.IsNullOrWhiteSpace(notes))
            {
                alert.ResolutionNotes = Truncate(notes, 2000);
            }
        }

        _audit.Record(
            AuditEntityTypeNames.Alert, alert.Id, auditAction, actorUserId,
            propertyName: nameof(Alert.Status),
            oldValue: previous.ToString(),
            newValue: target.ToString());
        await _context.SaveChangesAsync(cancellationToken);
        return OperationResult.Success();
    }

    public async Task<AlertGenerationResultDto> GenerateAsync(
        int windowDays, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var days = Math.Clamp(windowDays,
            AlertLimits.MinGenerationWindowDays, AlertLimits.MaxGenerationWindowDays);
        var windowEnd = _time.GetUtcNow().UtcDateTime.Date;
        var windowStart = windowEnd.AddDays(-(days - 1));

        var candidates = new List<Alert>();
        var keys = new HashSet<string>(StringComparer.Ordinal);

        await AddRiskCandidatesAsync(candidates, keys, windowStart, windowEnd, cancellationToken);
        await AddRelatedCaseCandidatesAsync(candidates, keys, windowStart, windowEnd, cancellationToken);
        await AddSecurityCandidatesAsync(candidates, keys, windowStart, windowEnd, cancellationToken);

        var capped = candidates.Take(AlertLimits.MaxGeneratedPerRun).ToList();

        // Deduplication check against persisted keys (the unique filtered index
        // on AlertKey is the database-side guarantee).
        var cappedKeys = capped.Select(a => a.AlertKey!).ToList();
        var existing = await _context.Alerts.AsNoTracking()
            .Where(a => a.AlertKey != null && cappedKeys.Contains(a.AlertKey))
            .Select(a => a.AlertKey!)
            .ToListAsync(cancellationToken);
        var existingSet = existing.ToHashSet(StringComparer.Ordinal);

        var created = 0;
        var skipped = 0;
        foreach (var alert in capped)
        {
            if (!existingSet.Add(alert.AlertKey!))
            {
                skipped++;
                continue;
            }

            _context.Alerts.Add(alert);
            await CreateNotificationsAsync(alert, cancellationToken);
            _audit.Record(
                AuditEntityTypeNames.Alert, alert.Id,
                AuditActionNames.AlertGenerated, actorUserId,
                propertyName: nameof(Alert.AlertKey),
                newValue: $"{AlertCategories.Label((AlertCategory)alert.AlertType)} · {alert.Title}");
            created++;
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new AlertGenerationResultDto
        {
            CreatedCount = created,
            SkippedDuplicateCount = skipped,
            WindowDays = days,
            WindowStart = windowStart,
            WindowEnd = windowEnd
        };
    }

    /// <summary>
    /// High-risk area indicators and anomaly candidates from the existing risk
    /// subsystem. The analytical basis is the explanation already generated there.
    /// </summary>
    private async Task AddRiskCandidatesAsync(
        List<Alert> candidates, HashSet<string> keys,
        DateTime windowStart, DateTime windowEnd, CancellationToken cancellationToken)
    {
        var analysis = await _risk.GetAnalysisAsync(new AnalyticsFilterCriteria
        {
            DateFrom = windowStart,
            DateTo = windowEnd
        }, cancellationToken);

        foreach (var indicator in analysis.Indicators)
        {
            if (indicator.InsufficientData || indicator.Level < 4)
            {
                continue;
            }

            var key = AlertKeys.Build(AlertCategory.HighRiskArea, "District",
                indicator.DistrictId, windowStart, windowEnd);
            if (!keys.Add(key))
            {
                continue;
            }

            var alert = NewGeneratedAlert(
                AlertCategory.HighRiskArea,
                indicator.Level >= 5 ? AlertSeverity.Critical : AlertSeverity.High,
                $"High-risk area — {indicator.DistrictName}",
                indicator.Explanation,
                windowStart, windowEnd, key);
            alert.RelatedEntityId = indicator.DistrictId;
            alert.RelatedEntityType = "District";
            candidates.Add(alert);
        }

        foreach (var anomaly in analysis.Anomalies)
        {
            var category = anomaly.AnomalyType == AnomalyTypes.VolumeSpike
                ? AlertCategory.CrimeSpike
                : AlertCategory.UnusualPattern;
            var key = AlertKeys.Build(category, "Anomaly", anomaly.DistrictId, windowStart, windowEnd)
                + $":{anomaly.AnomalyType}";
            if (!keys.Add(key))
            {
                continue;
            }

            var alert = NewGeneratedAlert(
                category,
                MapSeverity(anomaly.Severity),
                $"{AnomalyTypes.Label(anomaly.AnomalyType)} — {anomaly.DistrictName}",
                anomaly.Description,
                windowStart, windowEnd, key);
            alert.RelatedEntityId = anomaly.DistrictId;
            alert.RelatedEntityType = "District";
            candidates.Add(alert);
        }
    }

    /// <summary>
    /// Suspects recorded on multiple cases inside the window — relationships that
    /// already exist in NETRA case data, surfaced as leads, nothing inferred.
    /// </summary>
    private async Task AddRelatedCaseCandidatesAsync(
        List<Alert> candidates, HashSet<string> keys,
        DateTime windowStart, DateTime windowEnd, CancellationToken cancellationToken)
    {
        var rows = (await _context.CasePersons.AsNoTracking()
                .Where(cp => cp.Role == PersonRole.Suspect
                             && cp.CrimeCase.IncidentDate >= windowStart
                             && cp.CrimeCase.IncidentDate <= windowEnd)
                .Select(cp => new
                {
                    cp.PersonId,
                    cp.CrimeCaseId,
                    cp.Person.FirstName,
                    cp.Person.LastName
                })
                .ToListAsync(cancellationToken))
            .GroupBy(r => r.PersonId)
            .Select(g => new
            {
                PersonId = g.Key,
                CaseCount = g.Select(r => r.CrimeCaseId).Distinct().Count(),
                FirstName = g.First().FirstName,
                LastName = g.First().LastName
            })
            .Where(x => x.CaseCount >= 2)
            .OrderByDescending(x => x.CaseCount)
            .ThenBy(x => x.FirstName)
            .ThenBy(x => x.LastName)
            .Take(AlertLimits.MaxGeneratedPerRun)
            .ToList();

        foreach (var row in rows)
        {
            var key = AlertKeys.Build(AlertCategory.RelatedCases, "Person",
                row.PersonId, windowStart, windowEnd);
            if (!keys.Add(key))
            {
                continue;
            }

            var name = $"{row.FirstName} {row.LastName}";
            var alert = NewGeneratedAlert(
                AlertCategory.RelatedCases,
                AlertSeverity.Medium,
                $"Related cases — {name}",
                $"{name} is recorded as a suspect on {row.CaseCount} cases with incidents between " +
                $"{windowStart:d MMM yyyy} and {windowEnd:d MMM yyyy}. Association is recorded case " +
                "data, not a finding of involvement; review each case individually.",
                windowStart, windowEnd, key);
            alert.RelatedEntityId = row.PersonId;
            alert.RelatedEntityType = "Person";
            candidates.Add(alert);
        }
    }

    /// <summary>
    /// Failed sign-in attempts already present in the audit trail, surfaced as a
    /// security/system alert when they exceed the threshold within the window.
    /// </summary>
    private async Task AddSecurityCandidatesAsync(
        List<Alert> candidates, HashSet<string> keys,
        DateTime windowStart, DateTime windowEnd, CancellationToken cancellationToken)
    {
        var failures = await _context.AuditLogs.AsNoTracking()
            .CountAsync(a => a.Action == AuditActionNames.SignInFailed
                             && a.Timestamp >= windowStart
                             && a.Timestamp <= windowEnd.AddDays(1),
                cancellationToken);

        if (failures < SecuritySignInFailureThreshold)
        {
            return;
        }

        var key = AlertKeys.Build(AlertCategory.SecuritySystem, "Audit",
            "SignInFailed", windowStart, windowEnd);
        if (!keys.Add(key))
        {
            return;
        }

        candidates.Add(NewGeneratedAlert(
            AlertCategory.SecuritySystem,
            failures >= SecuritySignInFailureThreshold * 4 ? AlertSeverity.High : AlertSeverity.Medium,
            $"Repeated failed sign-in attempts ({failures})",
            $"The audit trail records {failures} failed sign-in attempts between " +
            $"{windowStart:d MMM yyyy} and {windowEnd:d MMM yyyy}. Review the audit log for detail; " +
            "this alert summarises recorded events and does not assess intent.",
            windowStart, windowEnd, key));
    }

    /// <summary>
    /// Creates in-application notifications for users whose role receives the
    /// alert category, respecting their stored <see cref="NotificationPreference"/>
    /// rows (no row means the default-enabled state). Idempotent per recipient and
    /// alert via the alert's stable key stored as the correlation id.
    /// </summary>
    private async Task CreateNotificationsAsync(Alert alert, CancellationToken cancellationToken)
    {
        var category = CategoryFor(alert.AlertType);
        var allRecipients = await GetRecipientsAsync(category, cancellationToken);

        // Disabled preferences are never bypassed.
        var recipients = allRecipients.Where(r => r.Enabled).ToList();

        // Truncate once so the stored value and the dedup lookup always agree.
        var correlated = Truncate(alert.AlertKey ?? alert.Id.ToString("N"), 100);
        var recipientIds = recipients.Select(u => u.Id).ToList();

        var alreadyNotified = await _context.Notifications.AsNoTracking()
            .Where(n => n.CorrelationId == correlated && recipientIds.Contains(n.RecipientId))
            .Select(n => n.RecipientId)
            .ToListAsync(cancellationToken);
        var notifiedSet = alreadyNotified.ToHashSet();

        foreach (var user in recipients)
        {
            if (notifiedSet.Contains(user.Id))
            {
                continue;
            }

            _context.Notifications.Add(new Notification
            {
                AlertId = alert.Id,
                RecipientId = user.Id,
                Title = Truncate(alert.Title, 200),
                Message = Truncate(alert.Description, 2000),
                NotificationType = (int)category,
                DeliveryChannel = 1, // in-application only; no SMTP in this phase
                CorrelationId = correlated,
                IsSent = true,
                SentAt = _time.GetUtcNow().UtcDateTime
            });
            _audit.Record(
                AuditEntityTypeNames.Notification, alert.Id,
                AuditActionNames.NotificationCreated, user.Id,
                newValue: Truncate(alert.Title, 200));
        }
    }

    private static NotificationCategory CategoryFor(int alertType) =>
        (AlertCategory)alertType switch
        {
            AlertCategory.RelatedCases => NotificationCategory.Investigation,
            AlertCategory.SecuritySystem => NotificationCategory.System,
            _ => NotificationCategory.CrimeIntelligence
        };

    private async Task<List<(Guid Id, bool Enabled)>> GetRecipientsAsync(
        NotificationCategory category, CancellationToken cancellationToken)
    {
        var roleName = category switch
        {
            NotificationCategory.Investigation => new[] { RecipientRoles.Administrator, RecipientRoles.Investigator, RecipientRoles.SeniorOfficer },
            NotificationCategory.System => new[] { RecipientRoles.Administrator },
            _ => new[] { RecipientRoles.Administrator, RecipientRoles.SeniorOfficer, RecipientRoles.Analyst }
        };

        var users = await _context.Users.AsNoTracking()
            .Join(_context.UserRoles.AsNoTracking(),
                u => u.Id, ur => ur.UserId,
                (u, ur) => new { User = u, RoleId = ur.RoleId })
            .Join(_context.Roles.AsNoTracking(),
                x => x.RoleId, r => r.Id,
                (x, r) => new { x.User, RoleName = r.Name })
            .Where(x => roleName.Contains(x.RoleName))
            .Select(x => new { x.User.Id, x.User.Email })
            .Distinct()
            .ToListAsync(cancellationToken);

        var typeValue = (int)category;
        var userIds = users.Select(u => u.Id).ToList();
        var disabled = await _context.NotificationPreferences.AsNoTracking()
            .Where(p => userIds.Contains(p.UserId)
                        && p.NotificationType == typeValue
                        && !p.IsEnabled)
            .Select(p => p.UserId)
            .ToListAsync(cancellationToken);
        var disabledSet = disabled.ToHashSet();

        return users
            .Select(u => (u.Id, Enabled: !disabledSet.Contains(u.Id)))
            .ToList();
    }

    private static AlertSeverity MapSeverity(string anomalySeverity) =>
        anomalySeverity switch
        {
            "Critical" => AlertSeverity.Critical,
            "High" => AlertSeverity.High,
            "Low" => AlertSeverity.Low,
            _ => AlertSeverity.Medium
        };

    private Alert NewGeneratedAlert(
        AlertCategory category, AlertSeverity severity, string title, string description,
        DateTime windowStart, DateTime windowEnd, string key) => new()
    {
        AlertType = (int)category,
        Severity = severity,
        Status = AlertStatus.New,
        Title = Truncate(title, 200),
        Description = Truncate(description, 2000),
        AlertKey = Truncate(key, 160),
        WindowStart = windowStart,
        WindowEnd = windowEnd,
        CreatedAt = _time.GetUtcNow().UtcDateTime
    };

    private static string Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty : (value.Length <= max ? value : value[..max]);
}
