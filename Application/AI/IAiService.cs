namespace NETRA.Application.AI;

/// <summary>
/// The request handed to a language-model provider. It contains only the
/// minimized, already-authorized facts produced by approved query services —
/// never raw entities, credentials or unrestricted data.
/// </summary>
public sealed record AiAnswerRequest
{
    public required string Question { get; init; }

    public required AssistantIntentKind Intent { get; init; }

    /// <summary>Plain-text summary of the query result, already data-minimized.</summary>
    public required string FactSummary { get; init; }

    /// <summary>False when the approved query found no records; the provider must say so.</summary>
    public required bool HasData { get; init; }

    public required int RecordCount { get; init; }
}

public sealed record AiAnswerResult(bool Succeeded, string? Text, string? Error = null);

/// <summary>
/// Abstraction over an AI response generator. Implementations only turn approved,
/// structured facts into natural language — authorization and data access happen
/// before this interface is ever reached.
/// </summary>
public interface IAiService
{
    string ProviderName { get; }

    Task<AiAnswerResult> GenerateAnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default);
}
