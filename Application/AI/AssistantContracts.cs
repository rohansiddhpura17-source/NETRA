namespace NETRA.Application.AI;

/// <summary>
/// The complete set of questions the assistant may answer. Every intent maps to
/// exactly one approved query operation on an existing application service — the
/// language model never chooses or builds database queries.
/// </summary>
public enum AssistantIntentKind
{
    Unknown = 0,
    CaseSearch = 1,
    CrimeTypeCount = 2,
    DistrictComparison = 3,
    PersonCaseLookup = 4,
    RecentCrimeTrend = 5,
    AnomalySummary = 6,
    CaseDetails = 7
}

/// <summary>A parsed question: an intent plus the filters extracted from the text.</summary>
public sealed class AssistantQuery
{
    public AssistantIntentKind Intent { get; init; }

    public Guid? CrimeTypeId { get; init; }

    public string? CrimeTypeName { get; init; }

    public Guid? PersonId { get; init; }

    public string? PersonName { get; init; }

    /// <summary>Analysis window in days parsed from phrases like "last 30 days".</summary>
    public int Days { get; init; } = AssistantLimits.DefaultWindowDays;

    public string? CaseNumber { get; init; }
}

public static class AssistantLimits
{
    public const int MinQuestionLength = 3;

    public const int MaxQuestionLength = 500;

    /// <summary>Rows handed to the AI per answer — aggregate/summary, never a full dump.</summary>
    public const int MaxResultRows = 10;

    public const int MaxSessionsDisplayed = 10;

    public const int MaxMessagesLoaded = 100;

    public const int DefaultWindowDays = 30;
}

public sealed record AssistantMessageDto(
    Guid Id,
    bool IsUser,
    string Content,
    DateTime CreatedAt,
    string? IntentLabel = null,
    string? ProviderName = null);

public sealed record AssistantSessionDto(
    Guid Id,
    string? Title,
    DateTime StartedAt,
    IReadOnlyList<AssistantMessageDto> Messages);

public sealed record AssistantSessionSummaryDto(
    Guid Id,
    string? Title,
    DateTime StartedAt,
    int TotalMessages);

public sealed class AssistantAskRequest
{
    public Guid? SessionId { get; init; }

    public string? Question { get; init; }
}
