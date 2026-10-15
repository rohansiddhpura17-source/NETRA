using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NETRA.Application.CaseOperations;
using NETRA.Domain.Alerts;
using NETRA.Domain.Cases;
using NETRA.Domain.Enums;
using NETRA.Domain.Intelligence;
using NETRA.Domain.Security;
using NETRA.Infrastructure.Data;

namespace NETRA.Infrastructure.Identity;

/// <summary>
/// Seeds realistic, synthetic demonstration crime cases into NETRA for development and demonstration.
/// Every record is explicitly marked [DEMO — SYNTHETIC DATA] and is 100% fictional.
/// Idempotent: checks for existing case numbers, alert keys, and assessment titles before creation.
/// </summary>
public sealed class SyntheticDemoDataSeeder
{
    public const string SeedSyntheticDemoCasesConfigKey = "DevelopmentSeed:SeedSyntheticDemoCases";
    public const string SeedSyntheticDemoCasesEnvVar = "NETRA_SEED_SYNTHETIC_DEMO_CASES";

    private readonly NetraDbContext _context;
    private readonly ICaseService _caseService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<SyntheticDemoDataSeeder> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public SyntheticDemoDataSeeder(
        NetraDbContext context,
        ICaseService caseService,
        UserManager<ApplicationUser> userManager,
        ILogger<SyntheticDemoDataSeeder> logger,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        _context = context;
        _caseService = caseService;
        _userManager = userManager;
        _logger = logger;
        _configuration = configuration;
        _environment = environment;
    }

    /// <summary>
    /// Checks whether demonstration seeding is authorized and enabled by configuration or environment variable.
    /// </summary>
    public bool IsEnabled()
    {
        if (!_environment.IsDevelopment())
        {
            return false;
        }

        var configValue = _configuration.GetValue<bool>(SeedSyntheticDemoCasesConfigKey, false);
        if (configValue)
        {
            return true;
        }

        var envValue = Environment.GetEnvironmentVariable(SeedSyntheticDemoCasesEnvVar);
        return string.Equals(envValue, "true", StringComparison.OrdinalIgnoreCase)
               || string.Equals(envValue, "1", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<int> SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!IsEnabled())
        {
            _logger.LogInformation(
                "Synthetic demo case seeding skipped: disabled by default or non-development environment. " +
                "Set {ConfigKey} or {EnvVar} to true to enable.",
                SeedSyntheticDemoCasesConfigKey, SeedSyntheticDemoCasesEnvVar);
            return 0;
        }

        _logger.LogInformation("Starting synthetic demo case seeding...");

        var districts = await _context.Districts.AsNoTracking().ToListAsync(cancellationToken);
        if (districts.Count == 0)
        {
            _logger.LogWarning("Synthetic demo case seeding aborted: no districts present.");
            return 0;
        }

        var crimeTypes = await _context.CrimeTypes.AsNoTracking().ToListAsync(cancellationToken);
        if (crimeTypes.Count == 0)
        {
            _logger.LogWarning("Synthetic demo case seeding aborted: no crime types present.");
            return 0;
        }

        var districtByCode = districts.ToDictionary(d => d.Code, StringComparer.OrdinalIgnoreCase);
        var crimeTypeByCode = crimeTypes.ToDictionary(ct => ct.Code, StringComparer.OrdinalIgnoreCase);

        Guid ResolveDistrictId(string code) =>
            districtByCode.TryGetValue(code, out var d) ? d.Id : districts[0].Id;

        Guid ResolveCrimeTypeId(string code) =>
            crimeTypeByCode.TryGetValue(code, out var ct) ? ct.Id : crimeTypes[0].Id;

        var adminUser = await _userManager.FindByEmailAsync("admin@netra.local")
                        ?? await _context.Users.FirstOrDefaultAsync(u => u.Email == "admin@netra.local", cancellationToken)
                        ?? await _context.Users.FirstOrDefaultAsync(cancellationToken);

        var actorId = adminUser?.Id ?? Guid.Parse("11111111-1111-4111-8111-111111111111");

        var demoCases = GetDemonstrationDefinitions(ResolveDistrictId, ResolveCrimeTypeId);
        var createdCases = 0;

        foreach (var def in demoCases)
        {
            var (caseId, wasCreated) = await EnsureCaseAsync(def, actorId, cancellationToken);
            if (wasCreated)
            {
                createdCases++;
            }

            if (caseId.HasValue)
            {
                if (def.RiskAssessment is not null)
                {
                    await EnsureRiskAssessmentAsync(caseId.Value, def.DistrictId, def.RiskAssessment, actorId, cancellationToken);
                }

                if (def.Alert is not null)
                {
                    await EnsureAlertAsync(caseId.Value, def.Alert, actorId, cancellationToken);
                }
            }
        }

        _logger.LogInformation("Synthetic demo case seeding complete. Processed {Total} definitions, {Created} newly created.",
            demoCases.Count, createdCases);

        return createdCases;
    }

    private async Task<(Guid? CaseId, bool Created)> EnsureCaseAsync(
        CaseSeedDefinition def,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        var existing = await _context.CrimeCases
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CaseNumber == def.CaseNumber, cancellationToken);

        if (existing is not null)
        {
            _logger.LogDebug("Demo case {CaseNumber} already present; skipping creation.", def.CaseNumber);
            return (existing.Id, false);
        }

        var request = new CreateCaseRequest
        {
            CaseNumber = def.CaseNumber,
            Title = def.Title,
            Description = def.Description,
            CrimeTypeId = def.CrimeTypeId,
            Status = def.Status.ToString(),
            Severity = def.Severity.ToString(),
            IncidentDate = def.IncidentDate,
            DistrictId = def.DistrictId,
            Address = def.Address,
            Latitude = def.Latitude,
            Longitude = def.Longitude,
            InvestigatorId = actorId,
            ModusOperandi = def.ModusOperandi
        };

        var result = await _caseService.CreateCaseAsync(request, actorId, cancellationToken);
        if (!result.Succeeded)
        {
            _logger.LogError("Failed to seed demo case {CaseNumber}: {Errors}",
                def.CaseNumber, string.Join("; ", result.Errors.Values.SelectMany(v => v)));
            return (null, false);
        }

        _logger.LogInformation("Seeded synthetic demo case {CaseNumber}: {Title}", def.CaseNumber, def.Title);
        return (result.Value, true);
    }

    private async Task EnsureRiskAssessmentAsync(
        Guid caseId,
        Guid districtId,
        RiskAssessmentSeedDefinition def,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        var exists = await _context.RiskAssessments
            .AsNoTracking()
            .AnyAsync(r => r.CrimeCaseId == caseId && r.Title == def.Title, cancellationToken);

        if (exists)
        {
            return;
        }

        var assessment = new RiskAssessment
        {
            CrimeCaseId = caseId,
            DistrictId = districtId,
            Title = def.Title,
            Assessment = def.Assessment,
            RiskLevel = def.RiskLevel,
            RiskScore = def.RiskScore,
            Factors = def.Factors,
            Recommendations = def.Recommendations,
            AssessedAt = def.AssessedAt,
            AssessedById = actorId,
            CreatedAt = def.AssessedAt,
            UpdatedAt = def.AssessedAt
        };

        _context.RiskAssessments.Add(assessment);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Seeded demo risk assessment for case {CaseId}: {Title} (Score {Score})",
            caseId, def.Title, def.RiskScore);
    }

    private async Task EnsureAlertAsync(
        Guid caseId,
        AlertSeedDefinition def,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        var exists = await _context.Alerts
            .AsNoTracking()
            .AnyAsync(a => a.AlertKey == def.AlertKey, cancellationToken);

        if (exists)
        {
            return;
        }

        var alert = new Alert
        {
            Title = def.Title,
            Description = def.Description,
            Severity = def.Severity,
            Status = def.Status,
            AlertType = def.AlertType,
            AlertKey = def.AlertKey,
            CrimeCaseId = caseId,
            AssignedToId = actorId,
            CreatedAt = def.CreatedAt
        };

        _context.Alerts.Add(alert);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Seeded demo alert {AlertKey}: {Title}", def.AlertKey, def.Title);
    }

    private static List<CaseSeedDefinition> GetDemonstrationDefinitions(
        Func<string, Guid> resolveDistrict,
        Func<string, Guid> resolveCrimeType)
    {
        return
        [
            // 1. Online payment fraud
            new CaseSeedDefinition
            {
                CaseNumber = "CC-2026-0004",
                Title = "[DEMO — SYNTHETIC DATA] Fraudulent payment gateway redirection and merchant credential spoofing",
                Description = "[DEMO — SYNTHETIC DATA] Fictional demonstration record. Synthetic report of intercepted UPI and payment gateway callbacks targeting retail vendor portals in Ahmedabad. No real financial entities or individuals involved.",
                CrimeTypeId = resolveCrimeType("FRD"),
                Status = CaseStatus.UnderInvestigation,
                Severity = CaseSeverity.High,
                IncidentDate = new DateTime(2026, 2, 14, 11, 30, 0, DateTimeKind.Utc),
                DistrictId = resolveDistrict("CD"),
                Address = "C.G. Road Commercial Center, Navrangpura, Ahmedabad, Gujarat 380009",
                Latitude = 23.0365,
                Longitude = 72.5611,
                ModusOperandi = "[DEMO — SYNTHETIC DATA] Adversary deployed synthetic reverse proxy mimicking legitimate payment aggregator to harvest merchant auth tokens.",
                Alert = new AlertSeedDefinition
                {
                    AlertKey = "DEMO-ALERT-CC-2026-0004",
                    Title = "[DEMO — SYNTHETIC DATA] High Severity: Payment gateway callback hijacking alert",
                    Description = "[DEMO — SYNTHETIC DATA] Intercepted DNS redirection signatures matching known rogue payment endpoints.",
                    Severity = AlertSeverity.High,
                    Status = AlertStatus.Acknowledged,
                    AlertType = 1,
                    CreatedAt = new DateTime(2026, 2, 14, 12, 15, 0, DateTimeKind.Utc)
                }
            },

            // 2. Unauthorized bank-account access
            new CaseSeedDefinition
            {
                CaseNumber = "CC-2026-0005",
                Title = "[DEMO — SYNTHETIC DATA] Unauthorized corporate netbanking session hijack and credential stuffing",
                Description = "[DEMO — SYNTHETIC DATA] Fictional demonstration record. Simulated credential stuffing attack leading to unauthorized token generation on a staging banking interface in Surat.",
                CrimeTypeId = resolveCrimeType("FRD"),
                Status = CaseStatus.UnderInvestigation,
                Severity = CaseSeverity.High,
                IncidentDate = new DateTime(2026, 3, 21, 14, 45, 0, DateTimeKind.Utc),
                DistrictId = resolveDistrict("SD"),
                Address = "Ring Road Financial Complex, Surat, Gujarat 395002",
                Latitude = 21.1850,
                Longitude = 72.8250,
                ModusOperandi = "[DEMO — SYNTHETIC DATA] Automated session token replay attack originating from compromised corporate VPN gateway.",
                Alert = new AlertSeedDefinition
                {
                    AlertKey = "DEMO-ALERT-CC-2026-0005",
                    Title = "[DEMO — SYNTHETIC DATA] High Severity: Multiple concurrent corporate login anomalies",
                    Description = "[DEMO — SYNTHETIC DATA] Geographically impossible travel logins detected across synthetic administrative tokens.",
                    Severity = AlertSeverity.High,
                    Status = AlertStatus.New,
                    AlertType = 3,
                    CreatedAt = new DateTime(2026, 3, 21, 15, 10, 0, DateTimeKind.Utc)
                }
            },

            // 3. Vehicle theft
            new CaseSeedDefinition
            {
                CaseNumber = "CC-2026-0006",
                Title = "[DEMO — SYNTHETIC DATA] Coordinated theft of commercial logistics fleet vehicles",
                Description = "[DEMO — SYNTHETIC DATA] Fictional demonstration record. Two synthetic transport trucks reported missing from a logistics warehouse depot with jammed GPS transponders.",
                CrimeTypeId = resolveCrimeType("THF"),
                Status = CaseStatus.Open,
                Severity = CaseSeverity.Medium,
                IncidentDate = new DateTime(2026, 4, 18, 3, 15, 0, DateTimeKind.Utc),
                DistrictId = resolveDistrict("ND"),
                Address = "S.G. Highway Logistics Corridor, Ahmedabad, Gujarat 382481",
                Latitude = 23.1120,
                Longitude = 72.5400,
                ModusOperandi = "[DEMO — SYNTHETIC DATA] RF frequency jammer used to disrupt telemetry during nighttime depot breach."
            },

            // 4. Residential burglary
            new CaseSeedDefinition
            {
                CaseNumber = "CC-2026-0007",
                Title = "[DEMO — SYNTHETIC DATA] High-value residential burglary series with lock bypass",
                Description = "[DEMO — SYNTHETIC DATA] Fictional demonstration record. Targeted forced entry into gated residential community properties while residents were on holiday.",
                CrimeTypeId = resolveCrimeType("BUR"),
                Status = CaseStatus.UnderInvestigation,
                Severity = CaseSeverity.High,
                IncidentDate = new DateTime(2026, 5, 10, 22, 0, 0, DateTimeKind.Utc),
                DistrictId = resolveDistrict("ND"),
                Address = "Bodakdev Residential Sector, Ahmedabad, Gujarat 380054",
                Latitude = 23.0420,
                Longitude = 72.5180,
                ModusOperandi = "[DEMO — SYNTHETIC DATA] Non-destructive cylinder lock bypass followed by selective asset removal.",
                RiskAssessment = new RiskAssessmentSeedDefinition
                {
                    Title = "[DEMO — SYNTHETIC DATA] Serial Residential Intrusion Assessment",
                    Assessment = "Cluster of mechanical lock bypasses within northern residential sector indicates organized perpetrator group.",
                    RiskLevel = 3,
                    RiskScore = 71.0m,
                    Factors = "Recurring entry signatures; timing correlated with holiday travel schedules.",
                    Recommendations = "Increase neighborhood night patrols and encourage smart surveillance integration.",
                    AssessedAt = new DateTime(2026, 5, 11, 9, 0, 0, DateTimeKind.Utc)
                },
                Alert = new AlertSeedDefinition
                {
                    AlertKey = "DEMO-ALERT-CC-2026-0007",
                    Title = "[DEMO — SYNTHETIC DATA] Cluster Alert: Serial burglary modus operandi detected",
                    Description = "[DEMO — SYNTHETIC DATA] Recurring cylinder lock tamper pattern identified across northern residential sector.",
                    Severity = AlertSeverity.Medium,
                    Status = AlertStatus.InProgress,
                    AlertType = 2,
                    CreatedAt = new DateTime(2026, 5, 11, 9, 30, 0, DateTimeKind.Utc)
                }
            },

            // 5. Missing-person report
            new CaseSeedDefinition
            {
                CaseNumber = "CC-2026-0008",
                Title = "[DEMO — SYNTHETIC DATA] Urgent welfare check and missing research scholar investigation",
                Description = "[DEMO — SYNTHETIC DATA] Fictional demonstration record. Synthetic welfare inquiry for an adult researcher last seen near university technology library.",
                CrimeTypeId = resolveCrimeType("ASL"),
                Status = CaseStatus.Open,
                Severity = CaseSeverity.Medium,
                IncidentDate = new DateTime(2026, 6, 5, 18, 0, 0, DateTimeKind.Utc),
                DistrictId = resolveDistrict("CD"),
                Address = "University Road, Navrangpura, Ahmedabad, Gujarat 380009",
                Latitude = 23.0380,
                Longitude = 72.5450,
                ModusOperandi = "[DEMO — SYNTHETIC DATA] Subject failed to return to accommodation; mobile device turned off at transit station."
            },

            // 6. Document forgery
            new CaseSeedDefinition
            {
                CaseNumber = "CC-2026-0009",
                Title = "[DEMO — SYNTHETIC DATA] Fabricated land registry deeds and forged municipal seals",
                Description = "[DEMO — SYNTHETIC DATA] Fictional demonstration record. Synthetic commercial land registry transaction involving fraudulent notarized stamp papers and counterfeit revenue seals.",
                CrimeTypeId = resolveCrimeType("FRD"),
                Status = CaseStatus.Closed,
                Severity = CaseSeverity.Medium,
                IncidentDate = new DateTime(2026, 7, 12, 10, 0, 0, DateTimeKind.Utc),
                DistrictId = resolveDistrict("WD"),
                Address = "Kalawad Road Sub-Registrar Vicinity, Rajkot, Gujarat 360005",
                Latitude = 22.2880,
                Longitude = 70.7720,
                ModusOperandi = "[DEMO — SYNTHETIC DATA] Counterfeiting of deceased landowner power of attorney with false witness affidavits."
            },

            // 7. Business email compromise
            new CaseSeedDefinition
            {
                CaseNumber = "CC-2026-0010",
                Title = "[DEMO — SYNTHETIC DATA] Executive email spoofing and international supplier payment diversion",
                Description = "[DEMO — SYNTHETIC DATA] Fictional demonstration record. Look-alike executive domain used to issue fraudulent invoice banking update to corporate accounting division.",
                CrimeTypeId = resolveCrimeType("FRD"),
                Status = CaseStatus.UnderInvestigation,
                Severity = CaseSeverity.High,
                IncidentDate = new DateTime(2026, 8, 4, 16, 20, 0, DateTimeKind.Utc),
                DistrictId = resolveDistrict("ED"),
                Address = "Alkapuri Financial Hub, Vadodara, Gujarat 390007",
                Latitude = 22.3110,
                Longitude = 73.1810,
                ModusOperandi = "[DEMO — SYNTHETIC DATA] Typosquat domain combined with internal mail routing rule manipulation to intercept invoices.",
                RiskAssessment = new RiskAssessmentSeedDefinition
                {
                    Title = "[DEMO — SYNTHETIC DATA] Corporate BEC Supply-Chain Risk Assessment",
                    Assessment = "Supply chain credential exposure indicates targeted spear-phishing campaign against manufacturing suppliers.",
                    RiskLevel = 4,
                    RiskScore = 84.0m,
                    Factors = "Domain impersonation with automated inbox forwarding rules.",
                    Recommendations = "Deploy DMARC quarantine policies and verify out-of-band banking account updates.",
                    AssessedAt = new DateTime(2026, 8, 5, 10, 0, 0, DateTimeKind.Utc)
                }
            },

            // 8. Street robbery
            new CaseSeedDefinition
            {
                CaseNumber = "CC-2026-0011",
                Title = "[DEMO — SYNTHETIC DATA] Armed robbery and snatching outside commercial banking outlet",
                Description = "[DEMO — SYNTHETIC DATA] Fictional demonstration record. Cash transit bag snatched by two masked motorcycle occupants following bank branch withdrawal.",
                CrimeTypeId = resolveCrimeType("ROB"),
                Status = CaseStatus.Open,
                Severity = CaseSeverity.High,
                IncidentDate = new DateTime(2026, 9, 2, 13, 10, 0, DateTimeKind.Utc),
                DistrictId = resolveDistrict("SD"),
                Address = "Athwa Lines Commercial Corridor, Surat, Gujarat 395001",
                Latitude = 21.1780,
                Longitude = 72.8050,
                ModusOperandi = "[DEMO — SYNTHETIC DATA] Physical surveillance on high-value cash withdrawals followed by two-wheeler getaway.",
                RiskAssessment = new RiskAssessmentSeedDefinition
                {
                    Title = "[DEMO — SYNTHETIC DATA] Commercial Corridor Physical Security Assessment",
                    Assessment = "Transit-adjacent cash snatching pattern requires targeted patrol allocation around banking clusters.",
                    RiskLevel = 4,
                    RiskScore = 86.0m,
                    Factors = "Daylight armed interception targeting unescorted cash deposits.",
                    Recommendations = "Coordinate police presence at branch peak hours and audit perimeter CCTV coverage.",
                    AssessedAt = new DateTime(2026, 9, 3, 11, 0, 0, DateTimeKind.Utc)
                }
            },

            // 9. Suspicious financial transactions
            new CaseSeedDefinition
            {
                CaseNumber = "CC-2026-0012",
                Title = "[DEMO — SYNTHETIC DATA] Multi-layered mule account structuring and rapid cross-border remittances",
                Description = "[DEMO — SYNTHETIC DATA] Fictional demonstration record. Synthetic analytical flag indicating 45 high-frequency structured deposits routed into shell account cluster.",
                CrimeTypeId = resolveCrimeType("FRD"),
                Status = CaseStatus.Open,
                Severity = CaseSeverity.Critical,
                IncidentDate = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc),
                DistrictId = resolveDistrict("CD"),
                Address = "GIFT City International Finance Zone, Gandhinagar, Gujarat 382355",
                Latitude = 23.1610,
                Longitude = 72.6840,
                ModusOperandi = "[DEMO — SYNTHETIC DATA] High-velocity micro-structuring below AML thresholds followed by instant virtual asset conversion.",
                RiskAssessment = new RiskAssessmentSeedDefinition
                {
                    Title = "[DEMO — SYNTHETIC DATA] High-Velocity Financial Laundering Pattern Assessment",
                    Assessment = "Multi-node mule structuring network spanning 4 financial institutions. Critical systemic severity.",
                    RiskLevel = 5,
                    RiskScore = 92.5m,
                    Factors = "45 structured payments under reporting limits; rapid hop across beneficiary accounts.",
                    Recommendations = "Issue urgent freeze notice to beneficiary clearing accounts and conduct link graph tracing.",
                    AssessedAt = new DateTime(2026, 10, 1, 10, 30, 0, DateTimeKind.Utc)
                },
                Alert = new AlertSeedDefinition
                {
                    AlertKey = "DEMO-ALERT-CC-2026-0012",
                    Title = "[DEMO — SYNTHETIC DATA] Critical Anomaly: High-velocity structuring pattern",
                    Description = "[DEMO — SYNTHETIC DATA] Automated detection flagged 45 sequential inbound transfers under 50,000 INR within 2 hours.",
                    Severity = AlertSeverity.Critical,
                    Status = AlertStatus.New,
                    AlertType = 1,
                    CreatedAt = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc)
                }
            },

            // 10. Digital impersonation
            new CaseSeedDefinition
            {
                CaseNumber = "CC-2026-0013",
                Title = "[DEMO — SYNTHETIC DATA] AI voice deepfake and senior law-enforcement impersonation extortion",
                Description = "[DEMO — SYNTHETIC DATA] Fictional demonstration record. Fraudsters utilized synthesized voice clones of senior officials to coerce immediate digital payments.",
                CrimeTypeId = resolveCrimeType("FRD"),
                Status = CaseStatus.Closed,
                Severity = CaseSeverity.Low,
                IncidentDate = new DateTime(2026, 10, 10, 15, 30, 0, DateTimeKind.Utc),
                DistrictId = resolveDistrict("ND"),
                Address = "Infocity IT Corridor, Gandhinagar, Gujarat 382007",
                Latitude = 23.1970,
                Longitude = 72.6280,
                ModusOperandi = "[DEMO — SYNTHETIC DATA] Deepfake audio clone played over VoIP call alongside fake arrest warrant images via instant messaging."
            }
        ];
    }

    private sealed class CaseSeedDefinition
    {
        public required string CaseNumber { get; init; }
        public required string Title { get; init; }
        public required string Description { get; init; }
        public required Guid CrimeTypeId { get; init; }
        public required CaseStatus Status { get; init; }
        public required CaseSeverity Severity { get; init; }
        public required DateTime IncidentDate { get; init; }
        public required Guid DistrictId { get; init; }
        public required string Address { get; init; }
        public required double Latitude { get; init; }
        public required double Longitude { get; init; }
        public required string ModusOperandi { get; init; }
        public RiskAssessmentSeedDefinition? RiskAssessment { get; init; }
        public AlertSeedDefinition? Alert { get; init; }
    }

    private sealed class RiskAssessmentSeedDefinition
    {
        public required string Title { get; init; }
        public required string Assessment { get; init; }
        public required int RiskLevel { get; init; }
        public required decimal RiskScore { get; init; }
        public required string Factors { get; init; }
        public required string Recommendations { get; init; }
        public required DateTime AssessedAt { get; init; }
    }

    private sealed class AlertSeedDefinition
    {
        public required string AlertKey { get; init; }
        public required string Title { get; init; }
        public required string Description { get; init; }
        public required AlertSeverity Severity { get; init; }
        public required AlertStatus Status { get; init; }
        public required int AlertType { get; init; }
        public required DateTime CreatedAt { get; init; }
    }
}
