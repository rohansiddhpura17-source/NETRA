using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NETRA.Domain.Enums;
using NETRA.Domain.Security;
using NETRA.Domain.Cases;

namespace NETRA.Domain.Alerts;

public class Alert
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    public AlertSeverity Severity { get; set; } = AlertSeverity.Medium;

    public AlertStatus Status { get; set; } = AlertStatus.New;

    public int AlertType { get; set; } = 1;

    /// <summary>
    /// Stable deduplication key for analytically generated alerts
    /// (category + source + analysis window). Null for manually raised items.
    /// </summary>
    [MaxLength(160)]
    public string? AlertKey { get; set; }

    /// <summary>Start of the analysed period, when generated.</summary>
    public DateTime? WindowStart { get; set; }

    /// <summary>End of the analysed period, when generated.</summary>
    public DateTime? WindowEnd { get; set; }

    public Guid? CrimeCaseId { get; set; }

    [ForeignKey(nameof(CrimeCaseId))]
    public virtual CrimeCase? CrimeCase { get; set; }

    public Guid? RelatedEntityId { get; set; }

    [MaxLength(100)]
    public string? RelatedEntityType { get; set; }

    public Guid? AssignedToId { get; set; }

    [ForeignKey(nameof(AssignedToId))]
    public virtual ApplicationUser? AssignedTo { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? AcknowledgedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public Guid? ResolvedById { get; set; }

    [ForeignKey(nameof(ResolvedById))]
    public virtual ApplicationUser? ResolvedBy { get; set; }

    [MaxLength(2000)]
    public string? ResolutionNotes { get; set; }

    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();
}

public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? AlertId { get; set; }

    [ForeignKey(nameof(AlertId))]
    public virtual Alert? Alert { get; set; }

    public Guid RecipientId { get; set; }

    [ForeignKey(nameof(RecipientId))]
    public virtual ApplicationUser Recipient { get; set; } = null!;

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Message { get; set; }

    public int NotificationType { get; set; } = 1;

    public int DeliveryChannel { get; set; } = 1;

    public bool IsRead { get; set; } = false;

    public DateTime? ReadAt { get; set; }

    public bool IsSent { get; set; } = false;

    public DateTime? SentAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(100)]
    public string? CorrelationId { get; set; }
}