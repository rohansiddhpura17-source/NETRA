using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NETRA.Domain.Cases;
using NETRA.Domain.Enums;
using NETRA.Domain.Geography;
using NETRA.Domain.Investigation;
using NETRA.Domain.People;
using NETRA.Domain.Security;
using NETRA.Infrastructure.Data;

namespace NETRA.Infrastructure.Identity;

/// <summary>
/// Deterministic development fixtures for the case module.
/// </summary>
/// <remarks>
/// Fixed identifiers and fixed dates, so repeated seeding produces identical data rather
/// than new random records on every run. All persons are fictional placeholders. Runs only
/// in the Development environment and only when a development password is configured.
/// </remarks>
public sealed class DemoCaseDataSeeder
{
    private readonly NetraDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<DemoCaseDataSeeder> _logger;

    public DemoCaseDataSeeder(
        NetraDbContext context,
        UserManager<ApplicationUser> userManager,
        ILogger<DemoCaseDataSeeder> logger)
    {
        _context = context;
        _userManager = userManager;
        _logger = logger;
    }

    /// <summary>Fixed ids keep seeded rows stable across runs.</summary>
    private static readonly Guid InvestigatorOneId = new("11111111-1111-4111-8111-111111111111");
    private static readonly Guid InvestigatorTwoId = new("22222222-2222-4222-8222-222222222222");

    public async Task SeedAsync(string password, CancellationToken cancellationToken = default)
    {
        // Idempotent: once demo cases exist, do nothing.
        if (await _context.CrimeCases.AnyAsync(cancellationToken))
        {
            _logger.LogInformation("Demo case data already present; skipping.");
            return;
        }

        var investigators = await EnsureInvestigatorsAsync(password, cancellationToken);
        var adminId = investigators.AdminId;

        var crimeTypes = await _context.CrimeTypes
            .Where(t => t.Code == "HOM" || t.Code == "THF" || t.Code == "ASL")
            .ToDictionaryAsync(t => t.Code, cancellationToken);

        var districts = await _context.Districts.ToDictionaryAsync(d => d.Code, cancellationToken);
        var stations = await _context.PoliceStations.ToDictionaryAsync(s => s.Code, cancellationToken);

        // Reference date is fixed so demo incidents never drift.
        var incidentBase = new DateTime(2026, 1, 12, 9, 0, 0, DateTimeKind.Utc);

        // ---------------------------------------------------------- persons
        var witness = new Person
        {
            FirstName = "Dana",
            LastName = "Whitfield",
            NationalIdNumber = "DEMO-0001",
            DateOfBirth = new DateTime(1985, 4, 12, 0, 0, 0, DateTimeKind.Utc),
            Gender = "Female",
            Email = "dana.whitfield@example.invalid",
            Notes = "Fictional demo record.",
            CreatedById = adminId,
            CreatedAt = incidentBase
        };

        var suspect = new Person
        {
            FirstName = "Marcus",
            LastName = "Ellery",
            NationalIdNumber = "DEMO-0002",
            DateOfBirth = new DateTime(1979, 11, 3, 0, 0, 0, DateTimeKind.Utc),
            Gender = "Male",
            Email = "marcus.ellery@example.invalid",
            Notes = "Fictional demo record.",
            CreatedById = adminId,
            CreatedAt = incidentBase
        };

        var informant = new Person
        {
            FirstName = "Priya",
            LastName = "Raman",
            NationalIdNumber = "DEMO-0003",
            DateOfBirth = new DateTime(1992, 7, 21, 0, 0, 0, DateTimeKind.Utc),
            Gender = "Female",
            Email = "priya.raman@example.invalid",
            Notes = "Fictional demo record.",
            CreatedById = adminId,
            CreatedAt = incidentBase
        };

        _context.Persons.AddRange(witness, suspect, informant);
        await _context.SaveChangesAsync(cancellationToken);

        // Alias for the suspect, to exercise the alias panel.
        _context.PersonAliases.Add(new PersonAlias
        {
            PersonId = suspect.Id,
            FirstName = "M. Ellery",
            LastName = "",
            AliasType = "Partial name",
            IsPrimaryAlias = false,
            CreatedById = adminId,
            CreatedAt = incidentBase
        });

        // ------------------------------------------------------- locations
        var sceneOne = new Location
        {
            Name = "14 Wharf Road",
            Type = "Address",
            Address = "14 Wharf Road",
            Latitude = 51.5072,
            Longitude = -0.1276,
            DistrictId = districts["CD"].Id,
            PoliceStationId = stations["CHQ"].Id,
            CreatedAt = incidentBase
        };

        var sceneTwo = new Location
        {
            Name = "Unit 7, 88 Fenwick Street",
            Type = "Address",
            Address = "Unit 7, 88 Fenwick Street",
            Latitude = 51.5231,
            Longitude = -0.1425,
            DistrictId = districts["ND"].Id,
            PoliceStationId = stations["NP"].Id,
            CreatedAt = incidentBase
        };

        var related = new Location
        {
            Name = "Central Station car park",
            Type = "Landmark",
            Address = "Central Station car park",
            DistrictId = districts["CD"].Id,
            PoliceStationId = stations["CHQ"].Id,
            CreatedAt = incidentBase
        };

        _context.Locations.AddRange(sceneOne, sceneTwo, related);
        await _context.SaveChangesAsync(cancellationToken);

        _context.PersonLocations.Add(new PersonLocation
        {
            PersonId = witness.Id,
            LocationId = sceneOne.Id,
            LocationType = "Residence",
            CreatedAt = incidentBase
        });

        // ----------------------------------------------------------- cases
        var caseOne = new CrimeCase
        {
            CaseNumber = "CC-2026-0001",
            Title = "Reported theft of stored property at warehouse",
            Description = "Fictional demo case. Stored property reported missing from a locked unit; entry point not yet established.",
            CrimeTypeId = crimeTypes["THF"].Id,
            Status = CaseStatus.UnderInvestigation,
            Severity = CaseSeverity.Medium,
            IncidentDate = incidentBase,
            ReportedDate = incidentBase.AddHours(6),
            CreatedById = adminId,
            CreatedAt = incidentBase,
            UpdatedAt = incidentBase,
            AssignedInvestigatorId = InvestigatorOneId,
            DepartmentId = null
        };

        var caseTwo = new CrimeCase
        {
            CaseNumber = "CC-2026-0002",
            Title = "Aggravated assault outside licensed premises",
            Description = "Fictional demo case. Assault reported following an argument outside licensed premises.",
            CrimeTypeId = crimeTypes["ASL"].Id,
            Status = CaseStatus.Open,
            Severity = CaseSeverity.High,
            IncidentDate = incidentBase.AddDays(3),
            ReportedDate = incidentBase.AddDays(3).AddHours(2),
            CreatedById = adminId,
            CreatedAt = incidentBase.AddDays(3),
            UpdatedAt = incidentBase.AddDays(3),
            AssignedInvestigatorId = InvestigatorTwoId
        };

        var caseThree = new CrimeCase
        {
            CaseNumber = "CC-2026-0003",
            Title = "Unresolved report — no action taken",
            Description = "Fictional demo case. Report reviewed and closed without further action.",
            CrimeTypeId = crimeTypes["HOM"].Id,
            Status = CaseStatus.Closed,
            Severity = CaseSeverity.Low,
            IncidentDate = incidentBase.AddDays(7),
            ReportedDate = incidentBase.AddDays(8),
            ClosedDate = incidentBase.AddDays(11),
            CreatedById = adminId,
            CreatedAt = incidentBase.AddDays(7),
            UpdatedAt = incidentBase.AddDays(11)
        };

        _context.CrimeCases.AddRange(caseOne, caseTwo, caseThree);
        await _context.SaveChangesAsync(cancellationToken);

        // ---------------------------------------------- case <-> location
        _context.CaseLocations.AddRange(
            new CaseLocation
            {
                CrimeCaseId = caseOne.Id,
                LocationId = sceneOne.Id,
                LocationRole = "Incident",
                Notes = "Primary scene.",
                AssociatedAt = incidentBase
            },
            new CaseLocation
            {
                CrimeCaseId = caseOne.Id,
                LocationId = related.Id,
                LocationRole = "Related",
                Notes = "Vehicle seen nearby.",
                AssociatedAt = incidentBase
            },
            new CaseLocation
            {
                CrimeCaseId = caseTwo.Id,
                LocationId = sceneTwo.Id,
                LocationRole = "Incident",
                AssociatedAt = incidentBase.AddDays(3)
            });

        // ----------------------------------------------- case <-> persons
        _context.CasePersons.AddRange(
            new CasePerson
            {
                CrimeCaseId = caseOne.Id,
                PersonId = witness.Id,
                Role = PersonRole.Witness,
                Notes = "Reported the missing property.",
                AssociatedAt = incidentBase,
                AssociatedById = adminId
            },
            new CasePerson
            {
                CrimeCaseId = caseOne.Id,
                PersonId = suspect.Id,
                Role = PersonRole.PersonOfInterest,
                Notes = "Seen in the area on the day.",
                AssociatedAt = incidentBase,
                AssociatedById = adminId
            },
            new CasePerson
            {
                CrimeCaseId = caseTwo.Id,
                PersonId = informant.Id,
                Role = PersonRole.Informant,
                Notes = "Provided a statement.",
                AssociatedAt = incidentBase.AddDays(4),
                AssociatedById = adminId
            });

        // ----------------------------------------------- investigations
        var investigationOne = new Investigation
        {
            CrimeCaseId = caseOne.Id,
            Title = "Investigation — CC-2026-0001",
            Summary = "Fictional demo. Reviewing access logs and nearby CCTV coverage.",
            StartedAt = incidentBase.AddHours(8),
            LeadInvestigatorId = InvestigatorOneId,
            Status = 1,
            CreatedAt = incidentBase,
            UpdatedAt = incidentBase
        };

        var investigationTwo = new Investigation
        {
            CrimeCaseId = caseTwo.Id,
            Title = "Investigation — CC-2026-0002",
            Summary = "Fictional demo. Awaiting scene examination results.",
            StartedAt = incidentBase.AddDays(3).AddHours(4),
            LeadInvestigatorId = InvestigatorTwoId,
            Status = 1,
            CreatedAt = incidentBase.AddDays(3),
            UpdatedAt = incidentBase.AddDays(3)
        };

        var investigationThree = new Investigation
        {
            CrimeCaseId = caseThree.Id,
            Title = "Investigation — CC-2026-0003",
            Summary = "Fictional demo. Closed without further action.",
            StartedAt = incidentBase.AddDays(8),
            CompletedAt = incidentBase.AddDays(11),
            LeadInvestigatorId = InvestigatorOneId,
            Status = 3,
            CreatedAt = incidentBase.AddDays(8),
            UpdatedAt = incidentBase.AddDays(11)
        };

        _context.Investigations.AddRange(investigationOne, investigationTwo, investigationThree);
        await _context.SaveChangesAsync(cancellationToken);

        // ------------------------------------- evidence, notes, timeline
        var evidenceOne = new Evidence
        {
            InvestigationId = investigationOne.Id,
            Title = "Storage unit access log export",
            Description = "Fictional demo evidence item.",
            EvidenceType = 3,
            EvidenceStatus = 1,
            CollectedAt = incidentBase.AddHours(10),
            ChainOfCustody = "Collected by I1, sealed, logged.",
            CollectedByUserId = InvestigatorOneId,
            CreatedAt = incidentBase
        };

        var evidenceTwo = new Evidence
        {
            InvestigationId = investigationTwo.Id,
            Title = "CCTV segment 14:20–14:45",
            EvidenceType = 2,
            EvidenceStatus = 2,
            CollectedAt = incidentBase.AddDays(3).AddHours(6),
            CollectedByUserId = InvestigatorTwoId,
            CreatedAt = incidentBase.AddDays(3)
        };

        _context.Evidence.AddRange(evidenceOne, evidenceTwo);

        _context.InvestigationNotes.AddRange(
            new InvestigationNote
            {
                InvestigationId = investigationOne.Id,
                Content = "Fictional demo note. Access log obtained from the unit operator and queued for review.",
                NoteType = 1,
                CreatedById = InvestigatorOneId,
                CreatedAt = incidentBase.AddHours(12)
            },
            new InvestigationNote
            {
                InvestigationId = investigationOne.Id,
                Content = "Fictional demo note. Two nearby cameras identified for the following morning.",
                NoteType = 4,
                CreatedById = adminId,
                CreatedAt = incidentBase.AddDays(1)
            },
            new InvestigationNote
            {
                InvestigationId = investigationTwo.Id,
                Content = "Fictional demo note. Scene examination scheduled.",
                NoteType = 2,
                CreatedById = InvestigatorTwoId,
                CreatedAt = incidentBase.AddDays(4)
            });

        _context.TimelineEvents.AddRange(
            new TimelineEvent
            {
                InvestigationId = investigationOne.Id,
                Title = "Incident reported",
                Description = "Fictional demo event.",
                EventDateTime = incidentBase,
                EventType = 1,
                Location = "14 Wharf Road",
                CreatedById = adminId,
                CreatedAt = incidentBase
            },
            new TimelineEvent
            {
                InvestigationId = investigationOne.Id,
                Title = "Access log requested",
                EventDateTime = incidentBase.AddHours(9),
                EventType = 2,
                CreatedById = InvestigatorOneId,
                CreatedAt = incidentBase.AddHours(9)
            },
            new TimelineEvent
            {
                InvestigationId = investigationOne.Id,
                Title = "Witness statement taken",
                EventDateTime = incidentBase.AddDays(1),
                EventType = 4,
                CreatedById = InvestigatorOneId,
                CreatedAt = incidentBase.AddDays(1)
            },
            new TimelineEvent
            {
                InvestigationId = investigationTwo.Id,
                Title = "Scene preserved",
                EventDateTime = incidentBase.AddDays(3).AddHours(3),
                EventType = 1,
                Location = "Unit 7, 88 Fenwick Street",
                CreatedById = InvestigatorTwoId,
                CreatedAt = incidentBase.AddDays(3)
            });

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Seeded 3 demo cases, 3 persons and 3 locations for development.");
    }

    private async Task<(Guid AdminId, string AdminEmail)> EnsureInvestigatorsAsync(
        string password,
        CancellationToken cancellationToken)
    {
        var adminEmail = "admin@netra.local";

        var admin = await _userManager.FindByEmailAsync(adminEmail);
        if (admin is not null)
        {
            return (admin.Id, adminEmail);
        }

        // Reuse the configured development password so demo data never introduces a
        // second hard-coded credential.
        var investigators = new[]
        {
            new ApplicationUser
            {
                Id = InvestigatorOneId,
                UserName = "r.alvarez@netra.local",
                Email = "r.alvarez@netra.local",
                FirstName = "Rosa",
                LastName = "Alvarez",
                BadgeNumber = "INV-1001",
                Rank = "Detective Sergeant",
                EmailConfirmed = true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            },
            new ApplicationUser
            {
                Id = InvestigatorTwoId,
                UserName = "j.okafor@netra.local",
                Email = "j.okafor@netra.local",
                FirstName = "Jonah",
                LastName = "Okafor",
                BadgeNumber = "INV-1002",
                Rank = "Detective",
                EmailConfirmed = true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            }
        };

        foreach (var investigator in investigators)
        {
            var result = await _userManager.CreateAsync(investigator, password);
            if (!result.Succeeded)
            {
                _logger.LogError(
                    "Failed to create demo investigator {Email}: {Errors}",
                    investigator.Email,
                    string.Join(", ", result.Errors.Select(e => e.Description)));
            }
            else
            {
                await _userManager.AddToRoleAsync(investigator, "Investigator");
            }
        }

        var adminUser = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            FirstName = "System",
            LastName = "Administrator",
            BadgeNumber = "ADMIN001",
            Rank = "System Administrator",
            EmailConfirmed = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var adminResult = await _userManager.CreateAsync(adminUser, password);

        if (!adminResult.Succeeded)
        {
            _logger.LogError(
                "Failed to create demo admin: {Errors}",
                string.Join(", ", adminResult.Errors.Select(e => e.Description)));
        }
        else
        {
            await _userManager.AddToRoleAsync(adminUser, "System Administrator");
            return (adminUser.Id, adminEmail);
        }

        var createdAdmin = await _userManager.FindByEmailAsync(adminEmail);
        return (createdAdmin?.Id ?? Guid.Empty, adminEmail);
    }
}
