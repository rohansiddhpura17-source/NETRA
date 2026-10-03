using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NETRA.Domain.Enums;
using NETRA.Domain.Security;
using NETRA.Domain.Cases;
using NETRA.Domain.People;
using NETRA.Domain.Intelligence;

namespace NETRA.Domain.Geography;

public class Location
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Type { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    [MaxLength(20)]
    public string? PostalCode { get; set; }

    public Guid? DistrictId { get; set; }

    [ForeignKey(nameof(DistrictId))]
    public virtual District? District { get; set; }

    public Guid? PoliceStationId { get; set; }

    [ForeignKey(nameof(PoliceStationId))]
    public virtual PoliceStation? PoliceStation { get; set; }

    public Guid? ParentLocationId { get; set; }

    [ForeignKey(nameof(ParentLocationId))]
    public virtual Location? ParentLocation { get; set; }

    public virtual ICollection<Location> ChildLocations { get; set; } = new List<Location>();

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<CaseLocation> CaseLocations { get; set; } = new List<CaseLocation>();

    public virtual ICollection<PersonLocation> PersonLocations { get; set; } = new List<PersonLocation>();

    public virtual ICollection<NetworkEntity> NetworkEntities { get; set; } = new List<NetworkEntity>();
}

public class District
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Code { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public double? CenterLatitude { get; set; }

    public double? CenterLongitude { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<Location> Locations { get; set; } = new List<Location>();

    public virtual ICollection<PoliceStation> PoliceStations { get; set; } = new List<PoliceStation>();
}

public class PoliceStation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Code { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(20)]
    public string? PhoneNumber { get; set; }

    [MaxLength(100)]
    public string? Email { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public Guid DistrictId { get; set; }

    [ForeignKey(nameof(DistrictId))]
    public virtual District District { get; set; } = null!;

    public Guid? CommandingOfficerId { get; set; }

    [ForeignKey(nameof(CommandingOfficerId))]
    public virtual ApplicationUser? CommandingOfficer { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<Location> Locations { get; set; } = new List<Location>();

    public virtual ICollection<ApplicationUser> AssignedOfficers { get; set; } = new List<ApplicationUser>();
}