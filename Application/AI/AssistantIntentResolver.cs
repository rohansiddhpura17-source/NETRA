using System.Text.RegularExpressions;

namespace NETRA.Application.AI;

public sealed record AssistantLookup(Guid Id, string Name);

/// <summary>Reference lists the resolver needs to map words in a question to ids.</summary>
public sealed class AssistantReferenceData
{
    public IReadOnlyList<AssistantLookup> CrimeTypes { get; init; } = Array.Empty<AssistantLookup>();

    public IReadOnlyList<AssistantLookup> Persons { get; init; } = Array.Empty<AssistantLookup>();
}

/// <summary>
/// Deterministic, keyword-driven intent classification. The question is treated as
/// untrusted input: anything that looks like an instruction to bypass controls,
/// execute SQL or dump data resolves to <see cref="AssistantIntentKind.Unknown"/>
/// and is answered with safe guidance instead of a query. No language model is
/// consulted for query selection.
/// </summary>
public static class AssistantIntentResolver
{
    private static readonly Regex CaseNumberPattern =
        new(@"\b[a-z]{2,3}-\d{4}-\d{1,6}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExplicitDaysPattern =
        new(@"\b(?:last|past|previous|recent)\s+(\d{1,3})\s*days?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Phrases that indicate a prompt-injection or out-of-scope request. Matching is
    /// intentionally broad: these questions receive the safe guidance answer and
    /// never reach an approved query operation or the AI provider with data.
    /// </summary>
    private static readonly string[] BlockedPhrases =
    {
        "ignore all", "ignore previous", "ignore any", "disregard", "bypass",
        "restrictions", "jailbreak", "system prompt",
        "password", "api key", "credential", "secret", "connection string",
        "execute", "select *", "select from", "insert into", "update set",
        "delete from", "drop table", "sql", "database schema", "raw table",
        "all database records", "every record", "show everything", "dump",
        "grant me", "admin privileges", "unrestricted"
    };

    private static readonly string[] AnomalySignals =
        { "unusual", "anomal", "hotspot", "high-risk", "high risk", "risk", "spike", "escalat" };

    private static readonly string[] DistrictSignals =
        { "district", "area", "areas", "ward", "neighbourhood", "neighborhood", "compare", "highest number" };

    private static readonly string[] TrendSignals =
        { "trend", "over time", "over the past", "over the last", "monthly", "increase", "decrease", "pattern" };

    private static readonly string[] PersonSignals =
        { "person", "suspect", "associated with", "linked to", "individual" };

    private static readonly string[] CountSignals =
        { "how many", "number of", "count of", "total number", "tally" };

    private static readonly string[] CaseSearchSignals =
        { "show", "list", "find", "cases", "case details", "records" };

    public static AssistantQuery Resolve(string question, AssistantReferenceData references)
    {
        var normalized = " " + question.ToLowerInvariant().Trim() + " ";

        if (BlockedPhrases.Any(phrase => normalized.Contains(phrase, StringComparison.Ordinal)))
        {
            return new AssistantQuery { Intent = AssistantIntentKind.Unknown };
        }

        var days = ParseDays(normalized);
        var crimeType = MatchCrimeType(normalized, references.CrimeTypes);

        var caseNumber = CaseNumberPattern.Match(question).Value;
        if (caseNumber.Length > 0)
        {
            return new AssistantQuery
            {
                Intent = AssistantIntentKind.CaseDetails,
                CaseNumber = caseNumber.ToUpperInvariant(),
                Days = days
            };
        }

        if (AnomalySignals.Any(signal => normalized.Contains(signal, StringComparison.Ordinal)))
        {
            return new AssistantQuery { Intent = AssistantIntentKind.AnomalySummary, Days = days };
        }

        if (DistrictSignals.Any(signal => normalized.Contains(signal, StringComparison.Ordinal)))
        {
            return new AssistantQuery
            {
                Intent = AssistantIntentKind.DistrictComparison,
                CrimeTypeId = crimeType?.Id,
                CrimeTypeName = crimeType?.Name,
                Days = days
            };
        }

        if (TrendSignals.Any(signal => normalized.Contains(signal, StringComparison.Ordinal)))
        {
            return new AssistantQuery
            {
                Intent = AssistantIntentKind.RecentCrimeTrend,
                CrimeTypeId = crimeType?.Id,
                CrimeTypeName = crimeType?.Name,
                Days = days
            };
        }

        var person = MatchPerson(normalized, references.Persons);
        if (person is not null ||
            PersonSignals.Any(signal => normalized.Contains(signal, StringComparison.Ordinal)))
        {
            return new AssistantQuery
            {
                Intent = AssistantIntentKind.PersonCaseLookup,
                PersonId = person?.Id,
                PersonName = person?.Name,
                Days = days
            };
        }

        if (CountSignals.Any(signal => normalized.Contains(signal, StringComparison.Ordinal)))
        {
            return new AssistantQuery
            {
                Intent = AssistantIntentKind.CrimeTypeCount,
                CrimeTypeId = crimeType?.Id,
                CrimeTypeName = crimeType?.Name,
                Days = days
            };
        }

        if (crimeType is not null ||
            CaseSearchSignals.Any(signal => normalized.Contains(signal, StringComparison.Ordinal)))
        {
            return new AssistantQuery
            {
                Intent = AssistantIntentKind.CaseSearch,
                CrimeTypeId = crimeType?.Id,
                CrimeTypeName = crimeType?.Name,
                Days = days
            };
        }

        return new AssistantQuery { Intent = AssistantIntentKind.Unknown, Days = days };
    }

    private static int ParseDays(string normalized)
    {
        var explicitDays = ExplicitDaysPattern.Match(normalized);
        if (explicitDays.Success && int.TryParse(explicitDays.Groups[1].Value, out var parsed))
        {
            return Math.Clamp(parsed, 1, 3650);
        }

        if (normalized.Contains("week", StringComparison.Ordinal)) return 7;
        if (normalized.Contains("month", StringComparison.Ordinal)) return 30;
        if (normalized.Contains("year", StringComparison.Ordinal)) return 365;
        return AssistantLimits.DefaultWindowDays;
    }

    private static AssistantLookup? MatchCrimeType(string normalized, IReadOnlyList<AssistantLookup> crimeTypes)
    {
        AssistantLookup? best = null;
        foreach (var type in crimeTypes)
        {
            foreach (var word in type.Name.ToLowerInvariant().Split(' ', '\'', '-'))
            {
                // Only distinctive words count; "offenses" or "violations" alone
                // would over-match generic questions.
                if (word.Length < 4 || word is "offenses" or "violations") continue;
                if (normalized.Contains(" " + word, StringComparison.Ordinal) ||
                    normalized.Contains(word + " ", StringComparison.Ordinal))
                {
                    if (best is null || type.Name.Length > best.Name.Length) best = type;
                    break;
                }
            }
        }

        return best;
    }

    private static AssistantLookup? MatchPerson(string normalized, IReadOnlyList<AssistantLookup> persons)
    {
        foreach (var person in persons.OrderByDescending(p => p.Name.Length))
        {
            if (normalized.Contains(person.Name.ToLowerInvariant(), StringComparison.Ordinal))
            {
                return person;
            }
        }

        return null;
    }
}
