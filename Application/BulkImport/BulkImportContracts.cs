using NETRA.Domain.Enums;

namespace NETRA.Application.BulkImport;

/// <summary>
/// Processing ceilings for bulk import. Every loop over uploaded content is bounded by
/// one of these values, so a hostile file cannot make the server read, hold or render an
/// unbounded amount of data.
/// </summary>
public static class BulkImportLimits
{
    /// <summary>Uploads larger than this are rejected before anything is stored.</summary>
    public const long MaxFileBytes = 2 * 1024 * 1024;

    /// <summary>Data rows accepted from one file. Reading stops at the first row over this.</summary>
    public const int MaxDataRows = 5_000;

    /// <summary>Rows rendered on the preview page. The rest stay stored, they are not lost.</summary>
    public const int MaxPreviewRows = 50;

    /// <summary>Issues persisted for one job. Beyond this the remainder is only counted.</summary>
    public const int MaxStoredIssues = 500;

    /// <summary>Issues rendered on a page.</summary>
    public const int MaxShownIssues = 100;

    /// <summary>Jobs listed as "recent" on the module landing page.</summary>
    public const int MaxRecentJobs = 10;

    /// <summary>Configured lookup values named on the requirements page.</summary>
    public const int MaxReferenceValues = 25;

    public const int MaxJobNameLength = 200;

    /// <summary>Extensions accepted. Only comma-separated case files are supported.</summary>
    public static readonly IReadOnlyCollection<string> AllowedExtensions = [".csv"];

    /// <summary>
    /// Content types accepted. A browser-supplied type is advisory and attacker
    /// controlled, so this list is a first filter only: the extension, the byte size and
    /// an actual parse of the text are what decide whether an upload is usable.
    /// </summary>
    public static readonly IReadOnlyCollection<string> AllowedContentTypes =
    [
        "text/csv", "application/csv", "text/plain", "application/octet-stream",
        "application/vnd.ms-excel", "application/vnd.excel"
    ];

    /// <summary>Character budget for the row text retained per staged record.</summary>
    public const int MaxRawRowChars = 4_000;
}

/// <summary>What one CSV column means, and how strict its values are.</summary>
public sealed class BulkImportColumn
{
    /// <summary>Header text, matched case-insensitively.</summary>
    public required string Name { get; init; }

    public required bool Required { get; init; }

    public required string Description { get; init; }

    /// <summary>Longest accepted value, taken from the mapped database column.</summary>
    public required int MaxLength { get; init; }

    /// <summary>How the value is interpreted, shown to the operator preparing a file.</summary>
    public required string Format { get; init; }

    /// <summary>
    /// Whether a rejected value may be echoed back to the operator. Free-text columns that
    /// can carry personal information report the problem without repeating the content.
    /// </summary>
    public bool SafeToEcho { get; init; }
}

/// <summary>
/// The crime-case CSV contract. Every column maps onto a field the domain already
/// stores, so importing cannot create data the case register has no place for.
/// </summary>
public static class BulkImportSchema
{
    /// <summary>Identifies the column set a job was validated against.</summary>
    public const string CaseImportType = "crime-case-v1";

    public static readonly IReadOnlyList<BulkImportColumn> Columns =
    [
        new()
        {
            Name = "CaseNumber", Required = true, MaxLength = 50, SafeToEcho = true,
            Description = "Unique case reference. Must not repeat an existing NETRA case number.",
            Format = "Text, up to 50 characters"
        },
        new()
        {
            Name = "Title", Required = true, MaxLength = 200, SafeToEcho = false,
            Description = "Short case description shown in the case register.",
            Format = "Text, up to 200 characters"
        },
        new()
        {
            Name = "CrimeType", Required = true, MaxLength = 100, SafeToEcho = true,
            Description = "Crime type must exist and be active in NETRA.",
            Format = "Crime type code (for example THF) or name (for example Theft)"
        },
        new()
        {
            Name = "IncidentDate", Required = true, MaxLength = 32, SafeToEcho = true,
            Description = "When the incident happened. Future dates are rejected.",
            Format = "ISO date, yyyy-MM-dd, optionally with yyyy-MM-dd HH:mm (UTC)"
        },
        new()
        {
            Name = "Severity", Required = true, MaxLength = 20, SafeToEcho = true,
            Description = "Case severity.",
            Format = "One of Low, Medium, High, Critical"
        },
        new()
        {
            Name = "Status", Required = true, MaxLength = 20, SafeToEcho = true,
            Description = "Case status as registered.",
            Format = "One of Open, UnderInvestigation, PendingReview, Closed, Archived, Reopened"
        },
        new()
        {
            Name = "Description", Required = false, MaxLength = 2000, SafeToEcho = false,
            Description = "Full narrative of the report.",
            Format = "Text, up to 2,000 characters"
        },
        new()
        {
            Name = "District", Required = false, MaxLength = 100, SafeToEcho = true,
            Description = "District the incident belongs to. Without it no incident location is created.",
            Format = "District code (for example CD) or name (for example Central District)"
        },
        new()
        {
            Name = "PoliceStation", Required = false, MaxLength = 100, SafeToEcho = true,
            Description = "Station registering the case. Must sit in the named district when one is given.",
            Format = "Station code (for example CHQ) or name (for example Central HQ)"
        },
        new()
        {
            Name = "Address", Required = false, MaxLength = 500, SafeToEcho = false,
            Description = "Scene address, stored as the incident location.",
            Format = "Text, up to 500 characters"
        },
        new()
        {
            Name = "Latitude", Required = false, MaxLength = 20, SafeToEcho = true,
            Description = "Scene latitude. Supply both coordinates or neither.",
            Format = "Decimal degrees between -90 and 90"
        },
        new()
        {
            Name = "Longitude", Required = false, MaxLength = 20, SafeToEcho = true,
            Description = "Scene longitude. Supply both coordinates or neither.",
            Format = "Decimal degrees between -180 and 180"
        }
    ];

    public static IReadOnlyList<string> AllNames { get; } = Columns.Select(c => c.Name).ToList();

    public static IReadOnlyList<string> RequiredNames { get; } =
        Columns.Where(c => c.Required).Select(c => c.Name).ToList();

    /// <summary>Looks a column up by header text, ignoring case and surrounding space.</summary>
    public static BulkImportColumn? Find(string? header)
    {
        var name = header?.Trim();

        return string.IsNullOrEmpty(name)
            ? null
            : Columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Stable identifiers for the rules a CSV can break. A code is stored with each issue so a
/// later phase (or an operator comparing two runs) can group problems without matching on
/// message wording, which may change. Codes are formatted <c>{Column}.{Rule}</c>, with
/// <see cref="RowField"/> standing in for the column when a whole row is at fault.
/// </summary>
public static class BulkImportIssueCodes
{
    /// <summary>Column placeholder for problems that concern the row, not one field.</summary>
    public const string RowField = "Row";

    public const string MalformedRow = "MALFORMED_ROW";

    public const string EmptyRow = "EMPTY_ROW";

    public const string ControlCharacter = "CONTROL_CHARACTER";

    public const string RequiredValueMissing = "REQUIRED_MISSING";

    public const string ValueTooLong = "VALUE_TOO_LONG";

    public const string DuplicateInFile = "DUPLICATE_IN_FILE";

    public const string CaseNumberExists = "CASE_NUMBER_EXISTS";

    public const string UnknownCrimeType = "CRIME_TYPE_UNKNOWN";

    public const string UnknownDistrict = "DISTRICT_UNKNOWN";

    public const string UnknownPoliceStation = "STATION_UNKNOWN";

    public const string StationDistrictMismatch = "STATION_DISTRICT_MISMATCH";

    public const string InvalidDateFormat = "DATE_INVALID";

    public const string DateInFuture = "DATE_IN_FUTURE";

    public const string InvalidStatus = "STATUS_INVALID";

    public const string InvalidSeverity = "SEVERITY_INVALID";

    public const string InvalidCoordinates = "COORDINATES_INVALID";

    public const string CoordinatesIncomplete = "COORDINATES_INCOMPLETE";

    public const string LocationWithoutDistrict = "LOCATION_WITHOUT_DISTRICT";

    /// <summary>Recovers the column a code was raised against; empty for a bare rule code.</summary>
    public static string SplitField(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return string.Empty;
        }

        var separator = code.IndexOf('.', StringComparison.Ordinal);

        return separator <= 0 ? code : code[..separator];
    }
}

/// <summary>Reference data an operator needs before writing a file.</summary>
public sealed class BulkImportRequirementsDto
{
    public IReadOnlyList<BulkImportColumn> Columns { get; init; } = BulkImportSchema.Columns;

    public long MaxFileBytes { get; init; } = BulkImportLimits.MaxFileBytes;

    public int MaxDataRows { get; init; } = BulkImportLimits.MaxDataRows;

    public int MaxPreviewRows { get; init; } = BulkImportLimits.MaxPreviewRows;

    public int MaxShownIssues { get; init; } = BulkImportLimits.MaxShownIssues;

    public IReadOnlyCollection<string> AllowedExtensions { get; init; } = BulkImportLimits.AllowedExtensions;

    /// <summary>Crime type codes and names currently configured, capped by the limits class.</summary>
    public IReadOnlyList<string> CrimeTypes { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Districts { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> PoliceStations { get; init; } = Array.Empty<string>();

    /// <summary>True when reference data was truncated to the display ceiling.</summary>
    public bool ReferenceDataTruncated { get; init; }
}

/// <summary>A bulk import job, as listed and as shown in a workflow header.</summary>
public sealed class BulkImportJobDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public ImportType ImportType { get; init; } = ImportType.CrimeCases;

    public ImportStatus Status { get; init; } = ImportStatus.Pending;

    public string FileName { get; init; } = string.Empty;

    public long FileSizeBytes { get; init; }

    public string ContentType { get; init; } = string.Empty;

    public int TotalRows { get; init; }

    public int ValidRows { get; init; }

    public int InvalidRows { get; init; }

    public int DuplicateRows { get; init; }

    public int WarningRows { get; init; }

    public int ImportedRows { get; init; }

    public int ErrorCount { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime StartedAt { get; init; }

    public DateTime? CompletedAt { get; init; }

    /// <summary>
    /// The operator who uploaded the file. A job can only be continued by its uploader, so
    /// this is also the operator who runs the import; the audit trail names them per stage.
    /// </summary>
    public string CreatedByName { get; init; } = string.Empty;

    /// <summary>The job is ready and permitted to run its import right now.</summary>
    public bool CanImport { get; init; }

    /// <summary>Why importing is not available, when it is not.</summary>
    public string? ImportBlockedBecause { get; init; }

    /// <summary>True while the job still has no validated rows, so validation is the next step.</summary>
    public bool AwaitingValidation { get; init; }
}

/// <summary>
/// One problem found in an uploaded file. A supplied value is only ever quoted inside
/// <see cref="Message"/>, and only for columns marked safe to echo: free-text columns can
/// carry personal information, so their content is described rather than repeated.
/// </summary>
public sealed class BulkImportIssueDto
{
    /// <summary>Data row the issue belongs to; 0 means the file as a whole.</summary>
    public int RowNumber { get; init; }

    public string Field { get; init; } = string.Empty;

    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public ImportIssueSeverity Severity { get; init; } = ImportIssueSeverity.Error;
}

/// <summary>A staged row as shown on the validation and preview pages.</summary>
public sealed class BulkImportRowDto
{
    public int RowNumber { get; init; }

    public ImportRecordStatus Status { get; init; }

    /// <summary>Column values for the preview table. Empty when the row text was truncated.</summary>
    public IReadOnlyList<string> Cells { get; init; } = Array.Empty<string>();

    /// <summary>Original row text, always available even when cells could not be rebuilt.</summary>
    public string RawText { get; init; } = string.Empty;

    /// <summary>True when <see cref="Cells"/> could not be rebuilt and only raw text is shown.</summary>
    public bool CellsUnavailable { get; init; }

    public string? FirstIssue { get; init; }

    public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();
}

/// <summary>Strongly typed outcome of validating one uploaded file.</summary>
public sealed class BulkImportValidationDto
{
    public BulkImportJobDto Job { get; init; } = new();

    public int TotalRows { get; init; }

    public int ValidRows { get; init; }

    public int InvalidRows { get; init; }

    public int DuplicateRows { get; init; }

    public int WarningRows { get; init; }

    public IReadOnlyList<BulkImportIssueDto> Issues { get; init; } = Array.Empty<BulkImportIssueDto>();

    /// <summary>True when more issues exist than are listed.</summary>
    public bool IssuesTruncated { get; init; }

    public IReadOnlyList<BulkImportRowDto> ProblemRows { get; init; } = Array.Empty<BulkImportRowDto>();

    public DateTime ValidatedAt { get; init; }

    public string SchemaName { get; init; } = BulkImportSchema.CaseImportType;
}

/// <summary>What the operator sees before confirming an import. Read-only by design.</summary>
public sealed class BulkImportPreviewDto
{
    public BulkImportJobDto Job { get; init; } = new();

    public IReadOnlyList<BulkImportRowDto> Rows { get; init; } = Array.Empty<BulkImportRowDto>();

    public int ValidRows { get; init; }

    public int DuplicateRows { get; init; }

    public int InvalidRows { get; init; }

    public int WarningRows { get; init; }

    public int TotalRows { get; init; }

    /// <summary>True when valid rows exist beyond those rendered.</summary>
    public bool MoreRowsBeyondPreview { get; init; }

    public IReadOnlyList<BulkImportIssueDto> Issues { get; init; } = Array.Empty<BulkImportIssueDto>();

    public bool IssuesTruncated { get; init; }

    public string SchemaName { get; init; } = BulkImportSchema.CaseImportType;
}

/// <summary>Result of an import run.</summary>
public sealed class BulkImportResultDto
{
    public BulkImportJobDto Job { get; init; } = new();

    public int ImportedRows { get; init; }

    public int SkippedDuplicateRows { get; init; }

    public int RejectedRows { get; init; }

    public int TotalRows { get; init; }

    /// <summary>Cases created, so the result page can link straight into the register.</summary>
    public IReadOnlyList<BulkImportImportedCaseDto> ImportedCases { get; init; } =
        Array.Empty<BulkImportImportedCaseDto>();

    public bool MoreCasesThanShown { get; init; }

    public IReadOnlyList<BulkImportIssueDto> Issues { get; init; } = Array.Empty<BulkImportIssueDto>();

    public bool IssuesTruncated { get; init; }

    public DateTime? CompletedAt { get; init; }

    public bool Succeeded { get; init; }
}

/// <summary>A case the import created.</summary>
public sealed class BulkImportImportedCaseDto
{
    public Guid CaseId { get; init; }

    public int RowNumber { get; init; }

    public string CaseNumber { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;
}

/// <summary>An uploaded file to register as a new job. Nothing is parsed here.</summary>
public sealed class UploadBulkImportRequest
{
    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Client-supplied name. Used as a display label only, never as a path.</summary>
    public string OriginalFileName { get; init; } = string.Empty;

    public string ContentType { get; init; } = string.Empty;

    public long Length { get; init; }

    public Stream Content { get; init; } = Stream.Null;
}
