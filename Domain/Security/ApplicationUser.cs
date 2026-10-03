using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using NETRA.Domain.Enums;
using NETRA.Domain.Settings;
using NETRA.Domain.Alerts;
using NETRA.Domain.Audit;
using NETRA.Domain.AI;

namespace NETRA.Domain.Security;

public class ApplicationUser : IdentityUser<Guid>
{
    [MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    [MaxLength(200)]
    public string FullName => $"{FirstName} {LastName}".Trim();

    [MaxLength(50)]
    public string? BadgeNumber { get; set; }

    [MaxLength(100)]
    public string? Rank { get; set; }

    public Guid? DepartmentId { get; set; }

    [ForeignKey(nameof(DepartmentId))]
    public virtual Department? Department { get; set; }

    public Guid? PoliceStationId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastLoginAt { get; set; }

    public virtual ICollection<UserSetting> UserSettings { get; set; } = new List<UserSetting>();

    public virtual ICollection<NotificationPreference> NotificationPreferences { get; set; } = new List<NotificationPreference>();

    public virtual ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();

    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    public virtual ICollection<AssistantSession> AssistantSessions { get; set; } = new List<AssistantSession>();
}

public class Department
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(20)]
    public string? Code { get; set; }

    public Guid? ParentDepartmentId { get; set; }

    [ForeignKey(nameof(ParentDepartmentId))]
    public virtual Department? ParentDepartment { get; set; }

    public virtual ICollection<Department> SubDepartments { get; set; } = new List<Department>();

    public virtual ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}