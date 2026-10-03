using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NETRA.Domain.Enums;
using NETRA.Domain.Security;
using NETRA.Domain.Cases;
using NETRA.Domain.People;
using NETRA.Domain.Geography;

namespace NETRA.Domain.Intelligence;

public class RiskAssessment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Case the assessment belongs to, or null for an area-level assessment
    /// produced by the risk analysis service against a whole district.
    /// </summary>
    public Guid? CrimeCaseId { get; set; }

    [ForeignKey(nameof(CrimeCaseId))]
    public virtual CrimeCase? CrimeCase { get; set; }

    /// <summary>District an area-level assessment refers to; null for case-level records.</summary>
    public Guid? DistrictId { get; set; }

    [ForeignKey(nameof(DistrictId))]
    public virtual District? District { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string? Assessment { get; set; }

    public int RiskLevel { get; set; } = 1;

    public decimal RiskScore { get; set; }

    [MaxLength(2000)]
    public string? Factors { get; set; }

    [MaxLength(2000)]
    public string? Recommendations { get; set; }

    public DateTime AssessedAt { get; set; } = DateTime.UtcNow;

    public Guid AssessedById { get; set; }

    [ForeignKey(nameof(AssessedById))]
    public virtual ApplicationUser AssessedBy { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}

public class Anomaly
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string? Description { get; set; }

    public AnomalySeverity Severity { get; set; } = AnomalySeverity.Medium;

    public int AnomalyType { get; set; } = 1;

    public Guid? RelatedEntityId { get; set; }

    [MaxLength(100)]
    public string? RelatedEntityType { get; set; }

    [MaxLength(2000)]
    public string? DetectedPatterns { get; set; }

    public decimal ConfidenceScore { get; set; }

    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

    public Guid? DetectedById { get; set; }

    [ForeignKey(nameof(DetectedById))]
    public virtual ApplicationUser? DetectedBy { get; set; }

    public int Status { get; set; } = 1;

    public DateTime? ResolvedAt { get; set; }

    public Guid? ResolvedById { get; set; }

    [ForeignKey(nameof(ResolvedById))]
    public virtual ApplicationUser? ResolvedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class NetworkEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public int EntityType { get; set; } = 1;

    [MaxLength(100)]
    public string? EntitySubType { get; set; }

    public Guid? PersonId { get; set; }

    [ForeignKey(nameof(PersonId))]
    public virtual Person? Person { get; set; }

    public Guid? CrimeCaseId { get; set; }

    [ForeignKey(nameof(CrimeCaseId))]
    public virtual CrimeCase? CrimeCase { get; set; }

    public Guid? LocationId { get; set; }

    [ForeignKey(nameof(LocationId))]
    public virtual Location? Location { get; set; }

    [MaxLength(2000)]
    public string? Attributes { get; set; }

    public double? CentralityScore { get; set; }

    public int RiskLevel { get; set; } = 1;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<NetworkRelationship> SourceRelationships { get; set; } = new List<NetworkRelationship>();

    public virtual ICollection<NetworkRelationship> TargetRelationships { get; set; } = new List<NetworkRelationship>();
}

public class NetworkRelationship
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SourceEntityId { get; set; }

    [ForeignKey(nameof(SourceEntityId))]
    public virtual NetworkEntity SourceEntity { get; set; } = null!;

    public Guid TargetEntityId { get; set; }

    [ForeignKey(nameof(TargetEntityId))]
    public virtual NetworkEntity TargetEntity { get; set; } = null!;

    public RelationshipType RelationshipType { get; set; } = RelationshipType.Associate;

    [MaxLength(500)]
    public string? Description { get; set; }

    public decimal Strength { get; set; } = 1.0m;

    public DateTime? EstablishedAt { get; set; }

    public DateTime? EndedAt { get; set; }

    public bool IsActive { get; set; } = true;

    [MaxLength(2000)]
    public string? Evidence { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Guid CreatedById { get; set; }

    [ForeignKey(nameof(CreatedById))]
    public virtual ApplicationUser CreatedBy { get; set; } = null!;
}