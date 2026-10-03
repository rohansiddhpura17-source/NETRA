using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NETRA.Domain.Enums;
using NETRA.Domain.Security;
using NETRA.Domain.Cases;
using NETRA.Domain.Intelligence;
using NETRA.Domain.Investigation;
using NETRA.Domain.Geography;

namespace NETRA.Domain.People;

public class Person
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? MiddleName { get; set; }

    [MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    [MaxLength(300)]
    public string FullName => $"{FirstName} {MiddleName} {LastName}".Replace("  ", " ").Trim();

    public DateTime? DateOfBirth { get; set; }

    [MaxLength(20)]
    public string? Gender { get; set; }

    [MaxLength(50)]
    public string? NationalIdNumber { get; set; }

    [MaxLength(50)]
    public string? PassportNumber { get; set; }

    [MaxLength(50)]
    public string? DriversLicenseNumber { get; set; }

    [MaxLength(200)]
    public string? Email { get; set; }

    [MaxLength(20)]
    public string? PhoneNumber { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Guid CreatedById { get; set; }

    [ForeignKey(nameof(CreatedById))]
    public virtual ApplicationUser CreatedBy { get; set; } = null!;

    public virtual ICollection<PersonAlias> Aliases { get; set; } = new List<PersonAlias>();

    public virtual ICollection<PersonLocation> PersonLocations { get; set; } = new List<PersonLocation>();

    public virtual ICollection<CasePerson> CasePersons { get; set; } = new List<CasePerson>();

    public virtual ICollection<NetworkEntity> NetworkEntities { get; set; } = new List<NetworkEntity>();

    public virtual ICollection<Evidence> EvidenceItems { get; set; } = new List<Evidence>();
}

public class PersonAlias
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PersonId { get; set; }

    [ForeignKey(nameof(PersonId))]
    public virtual Person Person { get; set; } = null!;

    [MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? MiddleName { get; set; }

    [MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    [MaxLength(300)]
    public string FullName => $"{FirstName} {MiddleName} {LastName}".Replace("  ", " ").Trim();

    [MaxLength(100)]
    public string? AliasType { get; set; }

    public bool IsPrimaryAlias { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Guid CreatedById { get; set; }

    [ForeignKey(nameof(CreatedById))]
    public virtual ApplicationUser CreatedBy { get; set; } = null!;
}

public class PersonLocation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PersonId { get; set; }

    [ForeignKey(nameof(PersonId))]
    public virtual Person Person { get; set; } = null!;

    public Guid LocationId { get; set; }

    [ForeignKey(nameof(LocationId))]
    public virtual Location Location { get; set; } = null!;

    [MaxLength(100)]
    public string? LocationType { get; set; }

    public DateTime? FromDate { get; set; }

    public DateTime? ToDate { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}