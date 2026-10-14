using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NETRA.Application.CaseOperations;
using NETRA.Domain.Alerts;
using NETRA.Domain.Enums;
using NETRA.Infrastructure.Data;
using NETRA.Web.Models.Intake;

namespace NETRA.Web.Controllers;

/// <summary>
/// Webhook receiver for automated field and citizen incident intake forms,
/// including Google Forms, external reporting kiosks, and partner feeds.
/// </summary>
[ApiController]
[Route("api/intake")]
public sealed class IntakeWebhookController : ControllerBase
{
    private readonly NetraDbContext _context;
    private readonly ICaseService _caseService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<IntakeWebhookController> _logger;

    public IntakeWebhookController(
        NetraDbContext context,
        ICaseService caseService,
        IConfiguration configuration,
        ILogger<IntakeWebhookController> logger)
    {
        _context = context;
        _caseService = caseService;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Health-check and connectivity test endpoint for webhook setup.
    /// </summary>
    [HttpGet("ping")]
    [AllowAnonymous]
    public IActionResult Ping()
    {
        return Ok(new
        {
            service = "NETRA Incident Intake Webhook",
            status = "Online",
            timestamp = DateTime.UtcNow,
            acceptedFormat = "application/json",
            submissionEndpoint = "/api/intake/webhook"
        });
    }

    /// <summary>
    /// Ingests a submission from Google Forms or web intake into NETRA.
    /// Creates a CrimeCase and an active operational Alert.
    /// </summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> IngestGoogleForm(
        [FromBody] GoogleFormIntakePayload payload,
        CancellationToken cancellationToken)
    {
        // 1. Require a configured webhook secret. Never accept intake
        // submissions when deployment security configuration is missing.
        var expectedToken = _configuration["Webhook:Token"];
        if (string.IsNullOrWhiteSpace(expectedToken))
        {
            _logger.LogError(
                "Intake webhook is unavailable because its security token is not configured.");

            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new GoogleFormIntakeResponse
                {
                    Success = false,
                    Message = "Incident intake is temporarily unavailable."
                });
        }

        if (!Request.Headers.TryGetValue("X-NETRA-WEBHOOK-TOKEN", out var incomingToken) ||
            !WebhookTokensMatch(incomingToken.ToString(), expectedToken))
        {
            _logger.LogWarning(
                "Rejected intake webhook due to invalid or missing security token.");

            return Unauthorized(new GoogleFormIntakeResponse
            {
                Success = false,
                Message = "Unauthorized: Invalid or missing webhook security token."
            });
        }

        if (payload is null)
        {
            return BadRequest(new GoogleFormIntakeResponse
            {
                Success = false,
                Message = "Submission body cannot be empty."
            });
        }

        try
        {
            // 2. Resolve or fallback CrimeType
            var normalizedCrimeType = (payload.CrimeType ?? string.Empty).Trim();
            var crimeType = await _context.CrimeTypes
                .AsNoTracking()
                .Where(t => t.IsActive && (t.Name.Contains(normalizedCrimeType) || t.Code.Contains(normalizedCrimeType)))
                .FirstOrDefaultAsync(cancellationToken);

            if (crimeType is null)
            {
                crimeType = await _context.CrimeTypes
                    .AsNoTracking()
                    .Where(t => t.IsActive)
                    .OrderBy(t => t.DisplayOrder)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (crimeType is null)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new GoogleFormIntakeResponse
                {
                    Success = false,
                    Message = "No active crime categories are configured in NETRA database."
                });
            }

            // 3. Resolve or fallback District
            var normalizedDistrict = (payload.District ?? string.Empty).Trim();
            var district = await _context.Districts
                .AsNoTracking()
                .Where(d => d.IsActive && (d.Name.Contains(normalizedDistrict) || d.Code.Contains(normalizedDistrict)))
                .FirstOrDefaultAsync(cancellationToken);

            if (district is null)
            {
                district = await _context.Districts
                    .AsNoTracking()
                    .Where(d => d.IsActive)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            // 4. Resolve Actor User for attribution (Admin or System account)
            var actorUserId = await _context.Users
                .AsNoTracking()
                .Where(u => u.Email == "admin@netra.local")
                .Select(u => u.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (actorUserId == Guid.Empty)
            {
                actorUserId = await _context.Users
                    .AsNoTracking()
                    .Select(u => u.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (actorUserId == Guid.Empty)
            {
                actorUserId = Guid.NewGuid();
            }

            // 5. Parse Incident Date
            DateTime incidentDate = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(payload.IncidentDate) &&
                DateTime.TryParse(payload.IncidentDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedDate))
            {
                incidentDate = parsedDate <= DateTime.UtcNow.AddDays(1) ? parsedDate : DateTime.UtcNow;
            }

            // 6. Parse Severity
            var severityStr = (payload.Severity ?? "Medium").Trim();
            if (!Enum.TryParse<CaseSeverity>(severityStr, ignoreCase: true, out var severity))
            {
                severity = CaseSeverity.Medium;
            }

            // 7. Assemble Structured Narrative & Modus Operandi
            var title = string.IsNullOrWhiteSpace(payload.Title)
                ? $"Field Intake: {crimeType.Name} ({DateTime.UtcNow:dd MMM yyyy})"
                : payload.Title.Trim();

            var narrativeBuilder = new System.Text.StringBuilder();
            if (!string.IsNullOrWhiteSpace(payload.Description))
            {
                narrativeBuilder.AppendLine(payload.Description.Trim());
            }

            if (!string.IsNullOrWhiteSpace(payload.SubmitterRole) || !string.IsNullOrWhiteSpace(payload.SubmitterContact))
            {
                narrativeBuilder.AppendLine();
                narrativeBuilder.AppendLine("--- Informant Information ---");
                if (!string.IsNullOrWhiteSpace(payload.SubmitterRole))
                {
                    narrativeBuilder.AppendLine($"Submitter Role: {payload.SubmitterRole.Trim()}");
                }
                if (!string.IsNullOrWhiteSpace(payload.SubmitterContact))
                {
                    narrativeBuilder.AppendLine($"Contact: {payload.SubmitterContact.Trim()}");
                }
            }

            if (!string.IsNullOrWhiteSpace(payload.EvidenceLinks))
            {
                narrativeBuilder.AppendLine();
                narrativeBuilder.AppendLine("--- External Evidence / Media Links ---");
                narrativeBuilder.AppendLine(payload.EvidenceLinks.Trim());
            }

            var modusOperandiBuilder = new System.Text.StringBuilder();
            if (!string.IsNullOrWhiteSpace(payload.ModusOperandi))
            {
                modusOperandiBuilder.AppendLine(payload.ModusOperandi.Trim());
            }

            if (!string.IsNullOrWhiteSpace(payload.SuspectDetails))
            {
                if (modusOperandiBuilder.Length > 0) modusOperandiBuilder.AppendLine();
                modusOperandiBuilder.AppendLine("Suspect / Vehicle Characteristics:");
                modusOperandiBuilder.AppendLine(payload.SuspectDetails.Trim());
            }

            // 8. Register the CrimeCase
            var caseRequest = new CreateCaseRequest
            {
                Title = title,
                Description = narrativeBuilder.ToString().Trim(),
                CrimeTypeId = crimeType.Id,
                Status = CaseStatus.Open.ToString(),
                Severity = severity.ToString(),
                IncidentDate = incidentDate,
                DistrictId = district?.Id,
                Address = payload.Address?.Trim(),
                Latitude = payload.Latitude,
                Longitude = payload.Longitude,
                ModusOperandi = modusOperandiBuilder.Length > 0 ? modusOperandiBuilder.ToString().Trim() : null
            };

            var caseResult = await _caseService.CreateCaseAsync(caseRequest, actorUserId, cancellationToken);
            if (!caseResult.Succeeded || caseResult.Value == Guid.Empty)
            {
                var errors = string.Join("; ", caseResult.Errors.SelectMany(e => e.Value));
                _logger.LogError("Failed to register case from intake webhook: {Errors}", errors);
                return BadRequest(new GoogleFormIntakeResponse
                {
                    Success = false,
                    Message = $"Case registration failed: {errors}"
                });
            }

            var createdCaseId = caseResult.Value;
            var createdCase = await _context.CrimeCases
                .AsNoTracking()
                .Where(c => c.Id == createdCaseId)
                .Select(c => new { c.Id, c.CaseNumber })
                .FirstOrDefaultAsync(cancellationToken);

            // 9. Generate an Operational Alert for the NETRA Dashboard
            var alertSeverity = severity switch
            {
                CaseSeverity.Critical => AlertSeverity.Critical,
                CaseSeverity.High => AlertSeverity.High,
                CaseSeverity.Low => AlertSeverity.Low,
                _ => AlertSeverity.Medium
            };

            var alert = new Alert
            {
                Id = Guid.NewGuid(),
                Title = $"New Field Intake: {title}",
                Description = $"Intake report ingested from external form. Category: {crimeType.Name}, District: {district?.Name ?? "Unspecified"}. Submitter: {payload.SubmitterRole ?? "Anonymous"}.",
                Severity = alertSeverity,
                Status = AlertStatus.New,
                CrimeCaseId = createdCaseId,
                CreatedAt = DateTime.UtcNow
            };

            _context.Alerts.Add(alert);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Ingested external intake submission into NETRA: Case {CaseNumber} ({CaseId}), Alert {AlertId}",
                createdCase?.CaseNumber, createdCaseId, alert.Id);

            return Ok(new GoogleFormIntakeResponse
            {
                Success = true,
                Message = "Incident intake successfully registered in NETRA.",
                CaseId = createdCaseId,
                CaseNumber = createdCase?.CaseNumber,
                AlertId = alert.Id,
                ProcessedAtUtc = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing intake webhook.");
            return StatusCode(StatusCodes.Status500InternalServerError, new GoogleFormIntakeResponse
            {
                Success = false,
                Message = "An internal server error occurred while processing the submission."
            });
        }
    }

    private static bool WebhookTokensMatch(string incomingToken, string expectedToken)
    {
        var incomingBytes = Encoding.UTF8.GetBytes(incomingToken);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedToken);

        return incomingBytes.Length == expectedBytes.Length &&
            CryptographicOperations.FixedTimeEquals(incomingBytes, expectedBytes);
    }
}
