using NETRA.Application.AI;

namespace NETRA.Infrastructure.AI;

/// <summary>
/// Deterministic, offline response formatter. It phrases ONLY the structured facts
/// produced by the approved query layer — it never invents records, and when there
/// are no facts it says so. This keeps NETRA fully functional without any external
/// AI provider (and is the mandatory fallback when a local server is unreachable).
/// </summary>
public sealed class MockAiService : IAiService
{
    public const string Guidance =
        "I can answer questions about authorized NETRA crime, case, person-association and analytical data. " +
        "Try asking about case counts by crime type, district comparisons, recent trends, a person's " +
        "associated cases, or unusual activity in an area.";

    public string ProviderName => "MockAiService (deterministic, offline)";

    public Task<AiAnswerResult> GenerateAnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var text = request.Intent == AssistantIntentKind.Unknown
            ? Guidance
            : Compose(request);

        return Task.FromResult(new AiAnswerResult(true, text));
    }

    private static string Compose(AiAnswerRequest request)
    {
        if (!request.HasData)
        {
            return "No matching NETRA records were found for this question within the authorized data set.";
        }

        var lead = request.Intent switch
        {
            AssistantIntentKind.AnomalySummary => "Analytical indication: ",
            AssistantIntentKind.PersonCaseLookup => "Based on the available NETRA case records, ",
            _ => "Based on the available NETRA records, "
        };

        return $"{lead}{request.FactSummary}\n\n" +
               $"Source: {request.RecordCount} authorized NETRA record(s) retrieved by the approved query layer. " +
               "This is decision-support output and must be verified against the underlying case records.";
    }
}
