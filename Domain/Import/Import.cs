using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NETRA.Domain.Enums;
using NETRA.Domain.Security;

namespace NETRA.Domain.Import;

public class ImportJob
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string? Description { get; set; }

    public ImportType ImportType { get; set; } = ImportType.CrimeCases;

    public ImportStatus Status { get; set; } = ImportStatus.Pending;

    [MaxLength(1000)]
    public string SourceFilePath { get; set; } = string.Empty;

    public long SourceFileSizeBytes { get; set; }

    [MaxLength(100)]
    public string SourceFileContentType { get; set; } = string.Empty;

    public int TotalRecords { get; set; }

    public int ProcessedRecords { get; set; }

    public int SuccessfulRecords { get; set; }

    public int FailedRecords { get; set; }

    [MaxLength(4000)]
    public string? Configuration { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }

    public Guid CreatedById { get; set; }

    [ForeignKey(nameof(CreatedById))]
    public virtual ApplicationUser CreatedBy { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<ImportRecord> Records { get; set; } = new List<ImportRecord>();

    public virtual ICollection<ImportError> Errors { get; set; } = new List<ImportError>();
}

public class ImportRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ImportJobId { get; set; }

    [ForeignKey(nameof(ImportJobId))]
    public virtual ImportJob ImportJob { get; set; } = null!;

    public int RecordNumber { get; set; }

    [MaxLength(4000)]
    public string RawData { get; set; } = string.Empty;

    public ImportRecordStatus Status { get; set; } = ImportRecordStatus.Pending;

    public Guid? CreatedEntityId { get; set; }

    [MaxLength(100)]
    public string? CreatedEntityType { get; set; }

    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }

    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

public class ImportError
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ImportJobId { get; set; }

    [ForeignKey(nameof(ImportJobId))]
    public virtual ImportJob ImportJob { get; set; } = null!;

    public int RecordNumber { get; set; }

    [MaxLength(100)]
    public string ErrorCode { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string ErrorMessage { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string? RawData { get; set; }

    public ImportIssueSeverity Severity { get; set; } = ImportIssueSeverity.Error;

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    public bool IsResolved { get; set; } = false;

    public DateTime? ResolvedAt { get; set; }

    public Guid? ResolvedById { get; set; }

    [ForeignKey(nameof(ResolvedById))]
    public virtual ApplicationUser? ResolvedBy { get; set; }
}