using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NETRA.Domain.Enums;
using NETRA.Domain.Security;

namespace NETRA.Domain.Audit;

public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(100)]
    public string EntityType { get; set; } = string.Empty;

    public Guid EntityId { get; set; }

    [MaxLength(50)]
    public string Action { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string? OldValues { get; set; }

    [MaxLength(4000)]
    public string? NewValues { get; set; }

    [MaxLength(200)]
    public string? PropertyName { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public Guid UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public virtual ApplicationUser User { get; set; } = null!;

    [MaxLength(100)]
    public string? IpAddress { get; set; }

    [MaxLength(500)]
    public string? UserAgent { get; set; }

    [MaxLength(100)]
    public string? CorrelationId { get; set; }

    [MaxLength(100)]
    public string? SessionId { get; set; }

    public bool IsSystemAction { get; set; } = false;
}