using System.ComponentModel.DataAnnotations;
using NETRA.Application.AI;

namespace NETRA.Web.Models.ViewModels.Assistant;

public sealed class AssistantViewModel
{
    public required IReadOnlyList<AssistantSessionSummaryDto> Sessions { get; init; }

    public AssistantSessionDto? ActiveSession { get; init; }

    public AskAssistantViewModel Ask { get; set; } = new();

    public bool HasConversation => ActiveSession is { Messages.Count: > 0 };

    public static AssistantViewModel Create(
        IReadOnlyList<AssistantSessionSummaryDto> sessions,
        AssistantSessionDto? activeSession) => new()
        {
            Sessions = sessions,
            ActiveSession = activeSession,
            Ask = new AskAssistantViewModel { SessionId = activeSession?.Id }
        };
}

public sealed class AskAssistantViewModel
{
    public Guid? SessionId { get; set; }

    [StringLength(AssistantLimits.MaxQuestionLength, ErrorMessage = "The question must be 500 characters or fewer.")]
    public string? Question { get; set; }
}
