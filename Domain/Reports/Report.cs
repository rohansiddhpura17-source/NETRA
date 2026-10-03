using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NETRA.Domain.Enums;
using NETRA.Domain.Security;
using NETRA.Domain.Cases;

namespace NETRA.Domain.Reports;

public class Report
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string? Description { get; set; }

    public ReportType ReportType { get; set; } = ReportType.Custom;

    public Guid? CrimeCaseId { get; set; }

    [ForeignKey(nameof(CrimeCaseId))]
    public virtual CrimeCase? CrimeCase { get; set; }

    [MaxLength(4000)]
    public string? Parameters { get; set; }

    [MaxLength(1000)]
    public string? GeneratedFilePath { get; set; }

    public long? GeneratedFileSizeBytes { get; set; }

    [MaxLength(100)]
    public string? GeneratedFileContentType { get; set; }

    public ReportStatus Status { get; set; } = ReportStatus.Draft;

    /// <summary>System-generated facts are immutable; analyst-authored content is editable.</summary>
    public ReportSource Source { get; set; } = ReportSource.AnalystAuthored;

    /// <summary>Analyst-authored observations. Never written for system-generated reports.</summary>
    [MaxLength(8000)]
    public string? Content { get; set; }

    /// <summary>Inclusive reporting period the report covers.</summary>
    public DateTime? PeriodFrom { get; set; }

    public DateTime? PeriodTo { get; set; }

    public DateTime? ArchivedAt { get; set; }

    public DateTime? GeneratedAt { get; set; }

    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    public Guid RequestedById { get; set; }

    [ForeignKey(nameof(RequestedById))]
    public virtual ApplicationUser RequestedBy { get; set; } = null!;

    public Guid? GeneratedById { get; set; }

    [ForeignKey(nameof(GeneratedById))]
    public virtual ApplicationUser? GeneratedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}