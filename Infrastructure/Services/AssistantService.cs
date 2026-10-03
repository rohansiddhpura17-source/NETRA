using Microsoft.EntityFrameworkCore;
using NETRA.Application.Abstractions;
using NETRA.Application.AI;
using NETRA.Application.Analytics;
using NETRA.Application.CaseOperations;
using NETRA.Application.Risk;
using NETRA.Domain.AI;
using NETRA.Infrastructure.AI;
using NETRA.Infrastructure.Data;
using System.Globalization;

namespace NETRA.Infrastructure.Services;

/// <summary>
/// The only path from a natural-language question to NETRA data:
/// validate → classify intent (deterministic resolver) → run ONE approved query
/// operation through existing application services → minimize the facts into a
/// short summary → ask the configured <see cref="IAiService"/> to phrase it →
/// persist the exchange and audit every step.
///
/// The AI provider never sees entities, credentials or unrestricted data and
/// never decides what to query. Unknown or unsafe questions receive fixed safe
/// guidance without any query or provider call. If the provider fails, the
/// deterministic <see cref="MockAiService"/> formats the same authorized facts.
/// </summary>
public sealed class AssistantService : IAssistantService
{
    private readonly NetraDbContext _context;
    private readonly ICaseService _cases;
    private readonly IAnalyticsService _analytics;
    private readonly IRiskAnalysisService _risk;
    private readonly IAiService _ai;
    private readonly MockAiService _fallbackAi;
    private readonly IAuditService _audit;
    private readonly TimeProvider _time;

    public AssistantService(
        NetraDbContext context,
        ICaseService cases,
        IAnalyticsService analytics,
        IRiskAnalysisService risk,
        IAiService ai,
        MockAiService fallbackAi,
        IAuditService audit,
        TimeProvider time)
    {
        _context = context;
        _cases = cases;
        _analytics = analytics;
        _risk = risk;
        _ai = ai;
        _fallbackAi = fallbackAi;
        _audit = audit;
        _time = time;
    }

    public async Task<AssistantSessionDto?> GetOwnSessionAsync(
        Guid sessionId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var session = await _context.AssistantSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        if (session is null)
        {
            return null;
        }

        if (session.UserId != actorUserId)
        {
            // Do not confirm existence of other users' conversations; audit the attempt.
            _audit.RecordSecurityEvent(
                AuditActionNames.AssistantAccessDenied, actorUserId,
                detail: $"Attempted to open session {sessionId:N}.");
            await _context.SaveChangesAsync(cancellationToken);
            return null;
        }

        var messages = await LoadMessagesAsync(sessionId, actorUserId, cancellationToken);
        return ToDto(session, messages);
    }

    public async Task<IReadOnlyList<AssistantSessionSummaryDto>> GetRecentSessionsAsync(
        Guid actorUserId, CancellationToken cancellationToken = default)
    {
        return await _context.AssistantSessions.AsNoTracking()
            .Where(s => s.UserId == actorUserId)
            .OrderByDescending(s => s.StartedAt)
            .ThenBy(s => s.Id)
            .Take(AssistantLimits.MaxSessionsDisplayed)
            .Select(s => new AssistantSessionSummaryDto(s.Id, s.Title, s.StartedAt, s.TotalMessages))
            .ToListAsync(cancellationToken);
    }

    public async Task<OperationResult<AssistantSessionDto>> StartSessionAsync(
        Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var session = NewSession(actorUserId);
        _context.AssistantSessions.Add(session);
        _audit.Record(
            AuditEntityTypeNames.AssistantSession, session.Id,
            AuditActionNames.AssistantSessionCreated, actorUserId,
            newValue: session.Title);
        await _context.SaveChangesAsync(cancellationToken);

        return OperationResult<AssistantSessionDto>.Success(ToDto(session, Array.Empty<AssistantMessage>()));
    }

    public async Task<OperationResult<AssistantSessionDto>> AskAsync(
        AssistantAskRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var question = (request.Question ?? string.Empty).Trim();

        if (question.Length < AssistantLimits.MinQuestionLength)
        {
            return OperationResult<AssistantSessionDto>.ValidationFailed(
                new Dictionary<string, string[]> { [nameof(AssistantAskRequest.Question)] = ["Enter a question to ask the assistant."] });
        }

        if (question.Length > AssistantLimits.MaxQuestionLength)
        {
            return OperationResult<AssistantSessionDto>.ValidationFailed(
                new Dictionary<string, string[]> { [nameof(AssistantAskRequest.Question)] = [$"The question must be {AssistantLimits.MaxQuestionLength} characters or fewer."] });
        }

        AssistantSession session;
        if (request.SessionId is { } sessionId)
        {
            var existing = await _context.AssistantSessions
                .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

            if (existing is null)
            {
                return OperationResult<AssistantSessionDto>.NotFound("That conversation was not found.");
            }

            if (existing.UserId != actorUserId)
            {
                _audit.RecordSecurityEvent(
                    AuditActionNames.AssistantAccessDenied, actorUserId,
                    detail: $"Attempted to post to session {sessionId:N}.");
                await _context.SaveChangesAsync(cancellationToken);
                return OperationResult<AssistantSessionDto>.NotFound("That conversation was not found.");
            }

            session = existing;
        }
        else
        {
            session = NewSession(actorUserId);
            _context.AssistantSessions.Add(session);
            _audit.Record(
                AuditEntityTypeNames.AssistantSession, session.Id,
                AuditActionNames.AssistantSessionCreated, actorUserId,
                newValue: "Created from first question");
        }

        if (string.IsNullOrWhiteSpace(session.Title) || session.Title == "New conversation")
        {
            session.Title = Truncate(question, 60);
        }

        _context.AssistantMessages.Add(new AssistantMessage
        {
            SessionId = session.Id,
            Role = 1,
            Content = Truncate(question, 8000)
        });
        _audit.Record(
            AuditEntityTypeNames.AssistantSession, session.Id,
            AuditActionNames.AssistantQuestionSubmitted, actorUserId,
            newValue: Truncate(question, 1000));

        var references = await GetReferenceDataAsync(cancellationToken);
        var query = AssistantIntentResolver.Resolve(question, references);

        string answer;
        string providerName;
        if (query.Intent == AssistantIntentKind.Unknown)
        {
            // Unsafe or out-of-scope questions never reach a query operation or the
            // AI provider; the fixed guidance text is the whole answer.
            answer = MockAiService.Guidance;
            providerName = _fallbackAi.ProviderName;
        }
        else if (query.Intent == AssistantIntentKind.PersonCaseLookup && query.PersonId is null)
        {
            answer = "Which person do you mean? Include the person's full name, for example " +
                     "\"Show cases associated with Jane Doe\".";
            providerName = _fallbackAi.ProviderName;
        }
        else
        {
            var facts = await RunApprovedQueryAsync(query, cancellationToken);

            _audit.Record(
                AuditEntityTypeNames.AssistantSession, session.Id,
                AuditActionNames.AssistantQueryExecuted, actorUserId,
                propertyName: query.Intent.ToString(),
                newValue: $"{facts.RecordCount} record(s), data found: {facts.HasData}");

            var (text, provider) = await GenerateAsync(new AiAnswerRequest
            {
                Question = Truncate(question, 1000),
                Intent = query.Intent,
                FactSummary = facts.Summary,
                HasData = facts.HasData,
                RecordCount = facts.RecordCount
            }, session.Id, actorUserId, cancellationToken);

            answer = text;
            providerName = provider;
        }

        _context.AssistantMessages.Add(new AssistantMessage
        {
            SessionId = session.Id,
            Role = 2,
            Content = Truncate(answer, 8000),
            Metadata = $"intent={query.Intent};provider={providerName}"
        });
        session.TotalMessages += 2;
        _audit.Record(
            AuditEntityTypeNames.AssistantSession, session.Id,
            AuditActionNames.AssistantResponseGenerated, actorUserId,
            newValue: Truncate(providerName, 200));

        // Commit first (messages + queued audit entries in one transaction), then
        // read the full conversation back for the DTO.
        await _context.SaveChangesAsync(cancellationToken);

        var messages = await LoadMessagesAsync(session.Id, actorUserId, cancellationToken);
        return OperationResult<AssistantSessionDto>.Success(ToDto(session, messages));
    }

    private async Task<(string Text, string Provider)> GenerateAsync(
        AiAnswerRequest request, Guid sessionId, Guid actorUserId, CancellationToken cancellationToken)
    {
        string? failure = null;
        try
        {
            var result = await _ai.GenerateAnswerAsync(request, cancellationToken);
            if (result.Succeeded && !string.IsNullOrWhiteSpace(result.Text))
            {
                return (result.Text!, _ai.ProviderName);
            }

            failure = result.Error ?? "The AI provider returned no answer.";
        }
        catch (Exception)
        {
            failure = "The AI provider threw an unexpected error.";
        }

        _audit.Record(
            AuditEntityTypeNames.AssistantSession, sessionId,
            AuditActionNames.AssistantProviderFailure, actorUserId,
            propertyName: _ai.ProviderName,
            newValue: Truncate(failure, 500));

        var fallback = await _fallbackAi.GenerateAnswerAsync(request, cancellationToken);
        return (fallback.Text ?? MockAiService.Guidance, $"{_fallbackAi.ProviderName} (fallback: {Truncate(failure, 120)})");
    }

    private record ApprovedFacts(bool HasData, int RecordCount, string Summary);

    private async Task<ApprovedFacts> RunApprovedQueryAsync(AssistantQuery query, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime.Date;
        var dateFrom = now.AddDays(-(query.Days - 1));
        var typeName = query.CrimeTypeName ?? "all crime types";

        switch (query.Intent)
        {
            case AssistantIntentKind.CaseSearch:
            {
                var result = await _cases.SearchCasesAsync(new CaseFilterCriteria
                {
                    CrimeTypeId = query.CrimeTypeId,
                    IncidentDateFrom = dateFrom,
                    IncidentDateTo = now,
                    PageSize = AssistantLimits.MaxResultRows
                }, cancellationToken);

                var total = result.Page.TotalCount;
                if (total == 0)
                {
                    return new ApprovedFacts(false, 0, string.Empty);
                }

                var rows = string.Join("; ", result.Page.Items.Select(c =>
                    $"{c.CaseNumber} — {c.Title} ({c.CrimeTypeName}, {Date(c.IncidentDate)}, {(c.DistrictName ?? "district not recorded")}, {c.Status})"));
                return new ApprovedFacts(true, total,
                    $"{total} {typeName} case(s) were recorded in the last {query.Days} days. Most recent: {rows}.");
            }

            case AssistantIntentKind.CrimeTypeCount:
            {
                var report = await _analytics.GetReportAsync(new AnalyticsFilterCriteria
                {
                    DateFrom = dateFrom,
                    DateTo = now,
                    CrimeTypeId = query.CrimeTypeId
                }, cancellationToken);

                return new ApprovedFacts(report.TotalCrimeVolume > 0, report.TotalCrimeVolume,
                    $"{report.TotalCrimeVolume} {typeName} case(s) were recorded between {Date(dateFrom)} and {Date(now)}.");
            }

            case AssistantIntentKind.DistrictComparison:
            {
                var report = await _analytics.GetReportAsync(new AnalyticsFilterCriteria
                {
                    DateFrom = dateFrom,
                    DateTo = now,
                    CrimeTypeId = query.CrimeTypeId
                }, cancellationToken);

                var districts = report.CrimeByDistrict.OrderByDescending(d => d.Total)
                    .Take(AssistantLimits.MaxResultRows).ToList();

                if (districts.Count == 0 || districts[0].Total == 0)
                {
                    return new ApprovedFacts(false, 0, string.Empty);
                }

                var breakdown = string.Join("; ", districts.Select(d => $"{d.DistrictName}: {d.Total}"));
                return new ApprovedFacts(true, report.TotalCrimeVolume,
                    $"{districts[0].DistrictName} recorded the highest number of {typeName} cases in the last {query.Days} days " +
                    $"({districts[0].Total} of {report.TotalCrimeVolume} total). Breakdown — {breakdown}.");
            }

            case AssistantIntentKind.RecentCrimeTrend:
            {
                var report = await _analytics.GetReportAsync(new AnalyticsFilterCriteria
                {
                    DateFrom = dateFrom,
                    DateTo = now,
                    CrimeTypeId = query.CrimeTypeId
                }, cancellationToken);

                if (report.TotalCrimeVolume == 0)
                {
                    return new ApprovedFacts(false, 0, string.Empty);
                }

                var trend = string.Join("; ", report.MonthlyTrend.Select(p => $"{p.Label}: {p.Count}"));
                var highlights = report.Highlights;
                return new ApprovedFacts(true, report.TotalCrimeVolume,
                    $"{report.TotalCrimeVolume} {typeName} case(s) were recorded in the last {query.Days} days. Monthly counts — {trend}. " +
                    (highlights.LeadingCrimeType is null
                        ? string.Empty
                        : $"Leading crime type: {highlights.LeadingCrimeType} ({highlights.LeadingCrimeTypeCount}). ") +
                    (highlights.HighestVolumeDistrict is null
                        ? string.Empty
                        : $"Highest-volume district: {highlights.HighestVolumeDistrict} ({highlights.HighestVolumeDistrictCount})."));
            }

            case AssistantIntentKind.AnomalySummary:
            {
                var analysis = await _risk.GetAnalysisAsync(new AnalyticsFilterCriteria
                {
                    DateFrom = dateFrom,
                    DateTo = now
                }, cancellationToken);

                var scored = analysis.Indicators
                    .Where(i => !i.InsufficientData && i.Score is not null)
                    .OrderByDescending(i => i.Score)
                    .Take(3).ToList();

                if (scored.Count == 0 && analysis.Anomalies.Count == 0)
                {
                    return new ApprovedFacts(false, 0, string.Empty);
                }

                var parts = scored.Select(i =>
                    $"{i.DistrictName} carries the highest area risk reading ({i.Score:0.#}/100, {i.LevelLabel}) — {i.Explanation}");
                var anomalies = analysis.Anomalies.Take(5).Select(a => $"{a.Title}: {a.Description}");
                var summary = string.Join(" ", parts.Union(anomalies));

                return new ApprovedFacts(true, analysis.TotalCasesInWindow,
                    $"over the last {query.Days} days ({analysis.TotalCasesInWindow} cases in the window): {summary}");
            }

            case AssistantIntentKind.PersonCaseLookup:
            {
                var personId = query.PersonId!.Value;
                var rows = await _context.CasePersons.AsNoTracking()
                    .Where(cp => cp.PersonId == personId)
                    .OrderByDescending(cp => cp.CrimeCase.IncidentDate)
                    .Take(AssistantLimits.MaxResultRows)
                    .Select(cp => new
                    {
                        cp.CrimeCase.CaseNumber,
                        cp.CrimeCase.Title,
                        cp.CrimeCase.IncidentDate,
                        Status = cp.CrimeCase.Status.ToString(),
                        cp.Role
                    })
                    .ToListAsync(cancellationToken);

                var total = await _context.CasePersons.AsNoTracking()
                    .CountAsync(cp => cp.PersonId == personId, cancellationToken);

                var name = query.PersonName ?? "That person";
                if (total == 0)
                {
                    return new ApprovedFacts(false, 0, string.Empty);
                }

                var list = string.Join("; ", rows.Select(r =>
                    $"{r.CaseNumber} — {r.Title} ({r.Role}, {Date(r.IncidentDate)}, {r.Status})"));
                return new ApprovedFacts(true, total,
                    $"{name} is associated with {total} NETRA case(s). Most recent: {list}. " +
                    "Case roles are recorded associations, not findings of involvement.");
            }

            case AssistantIntentKind.CaseDetails:
            {
                var result = await _cases.SearchCasesAsync(new CaseFilterCriteria
                {
                    Search = query.CaseNumber,
                    PageSize = 1
                }, cancellationToken);

                var match = result.Page.Items.FirstOrDefault(c =>
                    string.Equals(c.CaseNumber, query.CaseNumber, StringComparison.OrdinalIgnoreCase));

                if (match is null)
                {
                    return new ApprovedFacts(false, 0, string.Empty);
                }

                return new ApprovedFacts(true, 1,
                    $"case {match.CaseNumber} \"{match.Title}\" ({match.CrimeTypeName}, incident {Date(match.IncidentDate)}, " +
                    $"{match.Severity} severity, status {match.Status}, {(match.DistrictName ?? "district not recorded")}" +
                    (match.InvestigatorName is null ? ")." : $", investigator {match.InvestigatorName})."));
            }

            default:
                return new ApprovedFacts(false, 0, string.Empty);
        }
    }

    private async Task<AssistantReferenceData> GetReferenceDataAsync(CancellationToken cancellationToken)
    {
        var lookups = await _cases.GetLookupsAsync(cancellationToken);
        return new AssistantReferenceData
        {
            CrimeTypes = lookups.CrimeTypes.Select(t => new AssistantLookup(t.Id, t.Label)).ToList(),
            Persons = lookups.Persons.Select(p => new AssistantLookup(p.Id, p.Label)).ToList()
        };
    }

    private async Task<List<AssistantMessage>> LoadMessagesAsync(
        Guid sessionId, Guid actorUserId, CancellationToken cancellationToken)
    {
        // Ownership is re-checked in the query itself; the conversation is loaded
        // as the most recent window, oldest first.
        return await _context.AssistantMessages.AsNoTracking()
            .Where(m => m.Session!.UserId == actorUserId && m.SessionId == sessionId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(AssistantLimits.MaxMessagesLoaded)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    private AssistantSession NewSession(Guid actorUserId) => new()
    {
        UserId = actorUserId,
        Title = "New conversation",
        ModelName = Truncate(_ai.ProviderName, 100),
        StartedAt = _time.GetUtcNow().UtcDateTime
    };

    private static AssistantSessionDto ToDto(AssistantSession session, IReadOnlyCollection<AssistantMessage> messages) =>
        new(session.Id, session.Title, session.StartedAt, messages.Select(m =>
        {
            var (intent, provider) = ParseMetadata(m.Metadata);
            return new AssistantMessageDto(
                m.Id, m.Role == 1, m.Content, m.CreatedAt,
                m.Role == 2 ? intent : null, m.Role == 2 ? provider : null);
        }).ToList());

    private static (string? Intent, string? Provider) ParseMetadata(string? metadata)
    {
        if (metadata is null) return (null, null);
        var parts = metadata.Split(';', 2);
        var intent = parts[0].StartsWith("intent=", StringComparison.Ordinal) ? parts[0]["intent=".Length..] : null;
        var provider = parts.Length > 1 && parts[1].StartsWith("provider=", StringComparison.Ordinal)
            ? parts[1]["provider=".Length..]
            : null;
        return (intent, provider);
    }

    private static string Date(DateTime value) => value.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    private static string Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty : (value.Length <= max ? value : value[..max]);
}
