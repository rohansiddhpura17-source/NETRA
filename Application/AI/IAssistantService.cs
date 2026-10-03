using NETRA.Application.Abstractions;

namespace NETRA.Application.AI;

/// <summary>
/// Orchestrates the assistant conversation flow: validate, interpret intent,
/// run one approved query operation through existing application services,
/// minimize the facts, ask the AI provider to phrase them, and persist the
/// exchange. It is the only path from a user question to NETRA data.
/// </summary>
public interface IAssistantService
{
    /// <summary>Returns the session only when it belongs to <paramref name="actorUserId"/>.</summary>
    Task<AssistantSessionDto?> GetOwnSessionAsync(Guid sessionId, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AssistantSessionSummaryDto>> GetRecentSessionsAsync(Guid actorUserId, CancellationToken cancellationToken = default);

    Task<OperationResult<AssistantSessionDto>> StartSessionAsync(Guid actorUserId, CancellationToken cancellationToken = default);

    Task<OperationResult<AssistantSessionDto>> AskAsync(AssistantAskRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
}
