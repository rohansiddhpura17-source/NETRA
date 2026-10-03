using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NETRA.Domain.Enums;
using NETRA.Domain.Security;
using NETRA.Domain.Investigation;
using NETRA.Domain.Intelligence;
using NETRA.Domain.Alerts;
using NETRA.Domain.Reports;
using NETRA.Domain.Audit;
using NETRA.Domain.ImageInvestigation;
using NETRA.Domain.People;
using NETRA.Domain.Geography;

namespace NETRA.Domain.Cases;

public class CrimeCase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(50)]
    public string CaseNumber { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    public Guid CrimeTypeId { get; set; }

    [ForeignKey(nameof(CrimeTypeId))]
    public virtual CrimeType CrimeType { get; set; } = null!;

    public CaseStatus Status { get; set; } = CaseStatus.Open;

    public CaseSeverity Severity { get; set; } = CaseSeverity.Medium;

    public DateTime IncidentDate { get; set; }

    public DateTime? ReportedDate { get; set; }

    public DateTime? ClosedDate { get; set; }

    public Guid? AssignedInvestigatorId { get; set; }

    [ForeignKey(nameof(AssignedInvestigatorId))]
    public virtual ApplicationUser? AssignedInvestigator { get; set; }

    public Guid? SupervisingOfficerId { get; set; }

    [ForeignKey(nameof(SupervisingOfficerId))]
    public virtual ApplicationUser? SupervisingOfficer { get; set; }

    public Guid? DepartmentId { get; set; }

    [ForeignKey(nameof(DepartmentId))]
    public virtual Department? Department { get; set; }

    public Guid? PoliceStationId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Guid CreatedById { get; set; }

    [ForeignKey(nameof(CreatedById))]
    public virtual ApplicationUser CreatedBy { get; set; } = null!;

    public virtual ICollection<CasePerson> CasePersons { get; set; } = new List<CasePerson>();

    public virtual ICollection<CaseLocation> CaseLocations { get; set; } = new List<CaseLocation>();

    public virtual NETRA.Domain.Investigation.Investigation? Investigation { get; set; }

    public virtual ICollection<Alert> Alerts { get; set; } = new List<Alert>();

    public virtual ICollection<RiskAssessment> RiskAssessments { get; set; } = new List<RiskAssessment>();

    public virtual ICollection<Report> Reports { get; set; } = new List<Report>();

    public virtual ICollection<ImageRecord> ImageRecords { get; set; } = new List<ImageRecord>();
}

public class CrimeType
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Code { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public Guid? ParentCrimeTypeId { get; set; }

    [ForeignKey(nameof(ParentCrimeTypeId))]
    public virtual CrimeType? ParentCrimeType { get; set; }

    public virtual ICollection<CrimeType> SubCrimeTypes { get; set; } = new List<CrimeType>();

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<CrimeCase> Cases { get; set; } = new List<CrimeCase>();
}

public class CasePerson
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CrimeCaseId { get; set; }

    [ForeignKey(nameof(CrimeCaseId))]
    public virtual CrimeCase CrimeCase { get; set; } = null!;

    public Guid PersonId { get; set; }

    [ForeignKey(nameof(PersonId))]
    public virtual Person Person { get; set; } = null!;

    public PersonRole Role { get; set; } = PersonRole.PersonOfInterest;

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime AssociatedAt { get; set; } = DateTime.UtcNow;

    public Guid? AssociatedById { get; set; }

    [ForeignKey(nameof(AssociatedById))]
    public virtual ApplicationUser? AssociatedBy { get; set; }
}

public class CaseLocation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CrimeCaseId { get; set; }

    [ForeignKey(nameof(CrimeCaseId))]
    public virtual CrimeCase CrimeCase { get; set; } = null!;

    public Guid LocationId { get; set; }

    [ForeignKey(nameof(LocationId))]
    public virtual Location Location { get; set; } = null!;

    [MaxLength(100)]
    public string? LocationRole { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime AssociatedAt { get; set; } = DateTime.UtcNow;
}