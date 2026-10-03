using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NETRA.Domain.Enums;
using NETRA.Domain.Security;
using NETRA.Domain.Cases;
using NETRA.Domain.People;

namespace NETRA.Domain.Investigation;

public class Investigation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CrimeCaseId { get; set; }

    [ForeignKey(nameof(CrimeCaseId))]
    public virtual CrimeCase CrimeCase { get; set; } = null!;

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string? Summary { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }

    public Guid LeadInvestigatorId { get; set; }

    [ForeignKey(nameof(LeadInvestigatorId))]
    public virtual ApplicationUser LeadInvestigator { get; set; } = null!;

    public Guid? SupervisingOfficerId { get; set; }

    [ForeignKey(nameof(SupervisingOfficerId))]
    public virtual ApplicationUser? SupervisingOfficer { get; set; }

    public int Status { get; set; } = 1;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<InvestigationNote> Notes { get; set; } = new List<InvestigationNote>();

    public virtual ICollection<TimelineEvent> TimelineEvents { get; set; } = new List<TimelineEvent>();

    public virtual ICollection<Evidence> EvidenceItems { get; set; } = new List<Evidence>();
}

public class InvestigationNote
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid InvestigationId { get; set; }

    [ForeignKey(nameof(InvestigationId))]
    public virtual Investigation Investigation { get; set; } = null!;

    [MaxLength(4000)]
    public string Content { get; set; } = string.Empty;

    public int NoteType { get; set; } = 1;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Guid CreatedById { get; set; }

    [ForeignKey(nameof(CreatedById))]
    public virtual ApplicationUser CreatedBy { get; set; } = null!;

    public bool IsPrivate { get; set; } = false;
}

public class TimelineEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid InvestigationId { get; set; }

    [ForeignKey(nameof(InvestigationId))]
    public virtual Investigation Investigation { get; set; } = null!;

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    public DateTime EventDateTime { get; set; }

    public int EventType { get; set; } = 1;

    [MaxLength(100)]
    public string? Location { get; set; }

    public Guid? RelatedEntityId { get; set; }

    [MaxLength(100)]
    public string? RelatedEntityType { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Guid CreatedById { get; set; }

    [ForeignKey(nameof(CreatedById))]
    public virtual ApplicationUser CreatedBy { get; set; } = null!;
}

public class Evidence
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid InvestigationId { get; set; }

    [ForeignKey(nameof(InvestigationId))]
    public virtual Investigation Investigation { get; set; } = null!;

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    public int EvidenceType { get; set; } = 1;

    public int EvidenceStatus { get; set; } = 1;

    [MaxLength(100)]
    public string? CollectedBy { get; set; }

    public DateTime? CollectedAt { get; set; }

    [MaxLength(500)]
    public string? CollectionLocation { get; set; }

    [MaxLength(200)]
    public string? ChainOfCustody { get; set; }

    public Guid? CollectedByUserId { get; set; }

    [ForeignKey(nameof(CollectedByUserId))]
    public virtual ApplicationUser? CollectedByUser { get; set; }

    public Guid? PersonId { get; set; }

    [ForeignKey(nameof(PersonId))]
    public virtual Person? Person { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<EvidenceFile> Files { get; set; } = new List<EvidenceFile>();
}

public class EvidenceFile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EvidenceId { get; set; }

    [ForeignKey(nameof(EvidenceId))]
    public virtual Evidence Evidence { get; set; } = null!;

    [MaxLength(500)]
    public string FileName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string FileExtension { get; set; } = string.Empty;

    [MaxLength(100)]
    public string ContentType { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    [MaxLength(1000)]
    public string StoragePath { get; set; } = string.Empty;

    [MaxLength(64)]
    public string? ChecksumSha256 { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public Guid UploadedById { get; set; }

    [ForeignKey(nameof(UploadedById))]
    public virtual ApplicationUser UploadedBy { get; set; } = null!;

    public bool IsPrimary { get; set; } = false;

    [MaxLength(500)]
    public string? Description { get; set; }
}