using System.ComponentModel.DataAnnotations;

namespace NETRA.Web.Models.ViewModels.Cases;

/// <summary>
/// Register New Crime Case form. Bound to no domain entity: the controller maps this
/// to an application request.
/// </summary>
public sealed class CreateCaseViewModel
{
    [StringLength(50, MinimumLength = 4, ErrorMessage = "Case number must be 4 to 50 characters.")]
    [Display(Name = "Case number (optional)")]
    public string? CaseNumber { get; set; }

    [Required(ErrorMessage = "A case title is required.")]
    [StringLength(200, ErrorMessage = "The title cannot exceed 200 characters.")]
    public string Title { get; set; } = string.Empty;

    [StringLength(2000, ErrorMessage = "The description cannot exceed 2000 characters.")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Select a crime type.")]
    [Display(Name = "Crime type")]
    public Guid CrimeTypeId { get; set; }

    [Required(ErrorMessage = "Select a status.")]
    public string Status { get; set; } = Domain.Enums.CaseStatus.Open.ToString();

    [Required(ErrorMessage = "Select a severity.")]
    public string Severity { get; set; } = Domain.Enums.CaseSeverity.Medium.ToString();

    [Required(ErrorMessage = "An incident date is required.")]
    [DataType(DataType.Date)]
    [Display(Name = "Incident date")]
    public DateTime IncidentDate { get; set; } = DateTime.Today;

    [Display(Name = "District")]
    public Guid? DistrictId { get; set; }

    [StringLength(500)]
    [Display(Name = "Address")]
    public string? Address { get; set; }

    [Range(-90, 90, ErrorMessage = "Latitude must be between -90 and 90.")]
    public double? Latitude { get; set; }

    [Range(-180, 180, ErrorMessage = "Longitude must be between -180 and 180.")]
    public double? Longitude { get; set; }

    [Display(Name = "Investigator")]
    public Guid? InvestigatorId { get; set; }

    [StringLength(4000)]
    [Display(Name = "Modus operandi")]
    public string? ModusOperandi { get; set; }

    public IReadOnlyList<SelectOption> CrimeTypes { get; set; } = Array.Empty<SelectOption>();

    public IReadOnlyList<SelectOption> Districts { get; set; } = Array.Empty<SelectOption>();

    public IReadOnlyList<SelectOption> Investigators { get; set; } = Array.Empty<SelectOption>();

    public IReadOnlyList<SelectOption> StatusOptions { get; set; } = Array.Empty<SelectOption>();

    public IReadOnlyList<SelectOption> SeverityOptions { get; set; } = Array.Empty<SelectOption>();
}

public sealed class EditCaseViewModel
{
    [Required(ErrorMessage = "A case title is required.")]
    [StringLength(200, ErrorMessage = "The title cannot exceed 200 characters.")]
    public string Title { get; set; } = string.Empty;

    [StringLength(2000, ErrorMessage = "The description cannot exceed 2000 characters.")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Select a crime type.")]
    [Display(Name = "Crime type")]
    public Guid CrimeTypeId { get; set; }

    [Required(ErrorMessage = "Select a status.")]
    public string Status { get; set; } = string.Empty;

    [Required(ErrorMessage = "Select a severity.")]
    public string Severity { get; set; } = string.Empty;

    [Required(ErrorMessage = "An incident date is required.")]
    [DataType(DataType.Date)]
    [Display(Name = "Incident date")]
    public DateTime IncidentDate { get; set; }

    [StringLength(4000)]
    [Display(Name = "Modus operandi")]
    public string? ModusOperandi { get; set; }

    public IReadOnlyList<SelectOption> CrimeTypes { get; set; } = Array.Empty<SelectOption>();

    public IReadOnlyList<SelectOption> StatusOptions { get; set; } = Array.Empty<SelectOption>();

    public IReadOnlyList<SelectOption> SeverityOptions { get; set; } = Array.Empty<SelectOption>();
}