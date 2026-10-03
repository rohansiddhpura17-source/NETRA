using System.ComponentModel.DataAnnotations;
using NETRA.Application.CaseOperations;

namespace NETRA.Web.Models.ViewModels.Cases;

public sealed class CaseDetailsViewModel
{
    public CaseDetailDto Case { get; init; } = new();

    public IReadOnlyList<CaseChangeDto> History { get; init; } = Array.Empty<CaseChangeDto>();

    public bool CanEdit { get; init; }

    public bool CanManagePersons { get; init; }

    public bool CanManageLocations { get; init; }

    public bool CanManageEvidence { get; init; }

    public bool CanManageNotes { get; init; }

    public bool CanManageTimeline { get; init; }

    public bool CanAssign { get; init; }

    // Lookup options for the association panels.
    public IReadOnlyList<SelectOption> Persons { get; init; } = Array.Empty<SelectOption>();

    public IReadOnlyList<SelectOption> Locations { get; init; } = Array.Empty<SelectOption>();

    public IReadOnlyList<SelectOption> Investigators { get; init; } = Array.Empty<SelectOption>();
}

public sealed class AddPersonToCaseViewModel
{
    [Required(ErrorMessage = "Select a person.")]
    [Display(Name = "Person")]
    public Guid PersonId { get; set; }

    [Required(ErrorMessage = "Select a role.")]
    public string Role { get; set; } = Domain.Enums.PersonRole.PersonOfInterest.ToString();

    [StringLength(500)]
    public string? Notes { get; set; }

    public IReadOnlyList<SelectOption> Persons { get; set; } = Array.Empty<SelectOption>();

    public IReadOnlyList<SelectOption> RoleOptions { get; set; } = Array.Empty<SelectOption>();

    public static IReadOnlyList<SelectOption> Roles =>
        Enum.GetValues<Domain.Enums.PersonRole>()
            .Select(r => new SelectOption(r.ToString(), r.ToString()))
            .ToList();
}

public sealed class AddLocationToCaseViewModel
{
    [Required(ErrorMessage = "Select a location.")]
    [Display(Name = "Location")]
    public Guid LocationId { get; set; }

    [StringLength(100)]
    [Display(Name = "Role on case")]
    public string LocationRole { get; set; } = "Related";

    [StringLength(500)]
    public string? Notes { get; set; }

    public IReadOnlyList<SelectOption> Locations { get; set; } = Array.Empty<SelectOption>();
}

public sealed class AddEvidenceViewModel
{
    [Required(ErrorMessage = "An evidence name is required.")]
    [StringLength(200)]
    [Display(Name = "Evidence name")]
    public string Title { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Select an evidence type.")]
    [Display(Name = "Evidence type")]
    public string EvidenceType { get; set; } = "Physical";

    [DataType(DataType.Date)]
    [Display(Name = "Collected date")]
    public DateTime? CollectedAt { get; set; }

    [StringLength(500)]
    [Display(Name = "Collection location")]
    public string? CollectionLocation { get; set; }

    [StringLength(200)]
    [Display(Name = "Chain of custody")]
    public string? ChainOfCustody { get; set; }

    [Display(Name = "Attach file (optional)")]
    public IFormFile? File { get; set; }

    public static IReadOnlyList<SelectOption> EvidenceTypes { get; } =
    [
        new("Physical", "Physical"),
        new("Digital", "Digital"),
        new("Documentary", "Documentary"),
        new("Biological", "Biological"),
        new("Ballistic", "Ballistic"),
        new("Testimony", "Testimony")
    ];
}

public sealed class AddInvestigationNoteViewModel
{
    [Required(ErrorMessage = "Note content is required.")]
    [StringLength(4000, MinimumLength = 2, ErrorMessage = "Enter at least 2 characters.")]
    public string Content { get; set; } = string.Empty;

    [Required]
    public string NoteType { get; set; } = "Observation";

    [Display(Name = "Restrict to senior officers")]
    public bool IsPrivate { get; set; }

    public static IReadOnlyList<SelectOption> NoteTypes { get; } =
    [
        new("Observation", "Observation"),
        new("Analysis", "Analysis"),
        new("Interview", "Interview"),
        new("Action", "Action")
    ];
}

public sealed class AddTimelineEventViewModel
{
    [Required(ErrorMessage = "An event title is required.")]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    [Required(ErrorMessage = "An event date and time is required.")]
    [Display(Name = "Event date and time")]
    public DateTime EventDateTime { get; set; } = DateTime.Now;

    [Required]
    [Display(Name = "Event type")]
    public string EventType { get; set; } = "Incident";

    [StringLength(100)]
    public string? Location { get; set; }

    public static IReadOnlyList<SelectOption> EventTypes { get; } =
    [
        new("Incident", "Incident"),
        new("Report", "Report"),
        new("Warrant", "Warrant"),
        new("Interview", "Interview"),
        new("Forensic", "Forensic"),
        new("Court", "Court")
    ];
}

public sealed class AssignInvestigatorViewModel
{
    [Display(Name = "Investigator")]
    public Guid? InvestigatorId { get; set; }

    public IReadOnlyList<SelectOption> Investigators { get; set; } = Array.Empty<SelectOption>();
}