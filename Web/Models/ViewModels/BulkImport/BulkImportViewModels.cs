using System.ComponentModel.DataAnnotations;
using NETRA.Application.BulkImport;
using NETRA.Domain.Enums;
using NETRA.Web.Models.Ui;

namespace NETRA.Web.Models.ViewModels.BulkImport;

/// <summary>
/// The upload form. Bound fields are mapped onto
/// <see cref="UploadBulkImportRequest"/> by the controller; the service owns every rule.
/// </summary>
public sealed class BulkImportUploadForm
{
    [Display(Name = "Import name")]
    [Required(ErrorMessage = "Give this import a name so you can find it again.")]
    [StringLength(BulkImportLimits.MaxJobNameLength, ErrorMessage = "The import name is too long.")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Description (optional)")]
    [StringLength(4_000, ErrorMessage = "The description is too long.")]
    public string? Description { get; set; }

    [Display(Name = "CSV file")]
    public IFormFile? File { get; set; }
}

/// <summary>Landing page: what the module does, the CSV contract, and recent imports.</summary>
public sealed class BulkImportIndexViewModel
{
    public required BulkImportRequirementsDto Requirements { get; init; }

    public required IReadOnlyList<BulkImportJobDto> RecentJobs { get; init; }
}

/// <summary>The upload page.</summary>
public sealed class BulkImportUploadViewModel
{
    public required BulkImportRequirementsDto Requirements { get; init; }

    public BulkImportUploadForm Form { get; set; } = new();
}

/// <summary>Validation page: the strongly typed outcome for one file.</summary>
public sealed class BulkImportValidateViewModel
{
    public required BulkImportValidationDto Validation { get; init; }

    public IReadOnlyList<BulkImportIssueDto> ShownIssues { get; init; } = Array.Empty<BulkImportIssueDto>();

    public bool ShowImportButton { get; init; }
}

/// <summary>Preview page: read-only view of the rows an import would accept.</summary>
public sealed class BulkImportPreviewViewModel
{
    public required BulkImportPreviewDto Preview { get; init; }

    /// <summary>Column headers for the preview table, in the documented schema order.</summary>
    public IReadOnlyList<string> ColumnNames { get; init; } = BulkImportSchema.AllNames;
}

/// <summary>Result page: what an import run did.</summary>
public sealed class BulkImportResultViewModel
{
    public required BulkImportResultDto Result { get; init; }
}

/// <summary>
/// Server-side presentation mapping for the import enums. Views must not choose badge
/// tones themselves, so a status that appears on several pages reads the same everywhere.
/// </summary>
public static class BulkImportStatusUi
{
    public static StatusBadgeModel JobBadge(ImportStatus status) => new()
    {
        Text = status switch
        {
            ImportStatus.Pending => "Awaiting validation",
            ImportStatus.Validated => "Awaiting confirmation",
            ImportStatus.Processing => "Importing",
            ImportStatus.Completed => "Completed",
            ImportStatus.CompletedWithErrors => "Completed with errors",
            ImportStatus.Failed => "Failed",
            ImportStatus.Cancelled => "Cancelled",
            _ => "Unknown"
        },
        Tone = status switch
        {
            ImportStatus.Completed => NetraTone.Success,
            ImportStatus.CompletedWithErrors => NetraTone.Warning,
            ImportStatus.Failed => NetraTone.Danger,
            ImportStatus.Processing => NetraTone.Info,
            ImportStatus.Cancelled => NetraTone.Neutral,
            ImportStatus.Validated => NetraTone.Primary,
            _ => NetraTone.Warning
        }
    };

    public static StatusBadgeModel RowBadge(ImportRecordStatus status) => new()
    {
        Text = status switch
        {
            ImportRecordStatus.Valid => "Valid",
            ImportRecordStatus.Invalid => "Invalid",
            ImportRecordStatus.Duplicate => "Duplicate",
            ImportRecordStatus.Imported => "Imported",
            _ => "Pending"
        },
        Tone = status switch
        {
            ImportRecordStatus.Valid => NetraTone.Success,
            ImportRecordStatus.Imported => NetraTone.Info,
            ImportRecordStatus.Duplicate => NetraTone.Warning,
            ImportRecordStatus.Invalid => NetraTone.Danger,
            _ => NetraTone.Neutral
        }
    };

    public static string IssueLabel(ImportIssueSeverity severity) =>
        severity == ImportIssueSeverity.Warning ? "Warning" : "Error";

    public static NetraTone IssueTone(ImportIssueSeverity severity) =>
        severity == ImportIssueSeverity.Warning ? NetraTone.Warning : NetraTone.Danger;

    /// <summary>Human file size, computed here so no view performs arithmetic.</summary>
    public static string FileSize(long bytes) =>
        bytes >= 1024 * 1024
            ? $"{bytes / (1024.0 * 1024.0):0.##} MB"
            : bytes >= 1024
                ? $"{bytes / 1024.0:0.##} KB"
                : $"{bytes} B";

    public static string Rows(int count) => count == 1 ? "1 row" : $"{count:N0} rows";

    public static string When(DateTime value) => value.ToString("dd MMM yyyy HH:mm") + " UTC";

    public static string? When(DateTime? value) => value is null ? null : When(value.Value);
}
