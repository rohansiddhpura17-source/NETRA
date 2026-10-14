using System.Text.Json.Serialization;

namespace NETRA.Web.Models.Intake;

/// <summary>
/// Data transfer contract representing an incident or threat intake submission
/// dispatched from a Google Form or external web intake portal.
/// </summary>
public sealed class GoogleFormIntakePayload
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("crimeType")]
    public string? CrimeType { get; set; }

    [JsonPropertyName("severity")]
    public string? Severity { get; set; }

    [JsonPropertyName("incidentDate")]
    public string? IncidentDate { get; set; }

    [JsonPropertyName("district")]
    public string? District { get; set; }

    [JsonPropertyName("address")]
    public string? Address { get; set; }

    [JsonPropertyName("latitude")]
    public double? Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double? Longitude { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("modusOperandi")]
    public string? ModusOperandi { get; set; }

    [JsonPropertyName("suspectDetails")]
    public string? SuspectDetails { get; set; }

    [JsonPropertyName("evidenceLinks")]
    public string? EvidenceLinks { get; set; }

    [JsonPropertyName("submitterRole")]
    public string? SubmitterRole { get; set; }

    [JsonPropertyName("submitterContact")]
    public string? SubmitterContact { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("rawAnswers")]
    public Dictionary<string, string>? RawAnswers { get; set; }
}

public sealed class GoogleFormIntakeResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public Guid? CaseId { get; set; }
    public string? CaseNumber { get; set; }
    public Guid? AlertId { get; set; }
    public DateTime ProcessedAtUtc { get; set; } = DateTime.UtcNow;
}
