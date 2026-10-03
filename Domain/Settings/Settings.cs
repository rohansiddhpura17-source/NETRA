using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NETRA.Domain.Enums;
using NETRA.Domain.Security;

namespace NETRA.Domain.Settings;

public class UserSetting
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public virtual ApplicationUser User { get; set; } = null!;

    [MaxLength(100)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string? Value { get; set; }

    [MaxLength(100)]
    public string? Category { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class SecuritySetting
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(100)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string Value { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Category { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    public bool IsSystemSetting { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Guid? UpdatedById { get; set; }

    [ForeignKey(nameof(UpdatedById))]
    public virtual ApplicationUser? UpdatedBy { get; set; }
}

public class NotificationPreference
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public virtual ApplicationUser User { get; set; } = null!;

    public int NotificationType { get; set; } = 1;

    public int DeliveryChannel { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    [MaxLength(2000)]
    public string? Filters { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class SystemPreference
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(100)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string Value { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Category { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    public int ValueType { get; set; } = 1;

    public bool IsReadOnly { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Guid? UpdatedById { get; set; }

    [ForeignKey(nameof(UpdatedById))]
    public virtual ApplicationUser? UpdatedBy { get; set; }
}