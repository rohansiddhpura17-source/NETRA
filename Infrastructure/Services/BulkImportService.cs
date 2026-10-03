using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NETRA.Application.Abstractions;
using NETRA.Application.BulkImport;
using NETRA.Domain.Cases;
using NETRA.Domain.Enums;
using NETRA.Domain.Geography;
using NETRA.Domain.Import;
using NETRA.Domain.Investigation;
using NETRA.Domain.Security;
using NETRA.Infrastructure.Data;

namespace NETRA.Infrastructure.Services;

/// <summary>
/// Bulk data import for crime cases: a staged UPLOAD, VALIDATE, PREVIEW, IMPORT, RESULT
/// pipeline in which no row reaches the case register until an operator confirms it.
/// </summary>
/// <remarks>
/// Rules this class enforces:
/// <list type="bullet">
///   <item><description>Uploaded content is parsed, never executed. No cell is treated as a
///     path, formula or script; cells become parameterised column values only.</description></item>
///   <item><description>Reference values resolve through the existing domain lookups (crime
///     types, districts, police stations) and case numbers follow the existing unique index,
///     so an import cannot invent a second case model or a duplicate case.</description></item>
///   <item><description>Every loop is bounded: file size, row count, stored issues and shown
///     issues each have a ceiling, and reference data is loaded once per file rather than
///     once per row.</description></item>
///   <item><description>Validation and preview write nothing to the case tables. The import
///     commits its whole accepted set in one transaction and one SaveChangesAsync, so a
///     failure leaves no half-written register.</description></item>
///   <item><description>A job is the staging area of the operator who uploaded it; every
///     stage re-checks ownership and state, and every stage is audited without file
///     contents.</description></item>
/// </list>
/// </remarks>
public sealed class BulkImportService : IBulkImportService
{
    private const string IncidentLocationRole = "Incident";

    private const string CreatedEntityType = "CrimeCase";

    /// <summary>Storage sub-folder. The storage service sanitises it and confines writes inside the root.</summary>
    private const string StorageFolder = "bulk-import";

    /// <summary>Reference rows indexed per file. Well above any realistic configuration, and bounded.</summary>
    private const int MaxReferenceRows = 1_000;

    private const int CaseNumberChunkSize = 200;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string[] AcceptedDateFormats =
    [
        "yyyy-MM-dd", "yyyy-M-d",
        "yyyy-MM-dd HH:mm", "yyyy-M-d H:mm",
        "yyyy-MM-dd HH:mm:ss", "yyyy-M-d H:mm:ss",
        "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssZ"
    ];

    private readonly NetraDbContext _context;
    private readonly IFileStorageService _storage;
    private readonly IAuditService _audit;
    private readonly TimeProvider _time;

    public BulkImportService(
        NetraDbContext context,
        IFileStorageService storage,
        IAuditService audit,
        TimeProvider time)
    {
        _context = context;
        _storage = storage;
        _audit = audit;
        _time = time;
    }

    // ------------------------------------------------------------------- reference data

    public async Task<BulkImportRequirementsDto> GetRequirementsAsync(
        CancellationToken cancellationToken = default)
    {
        var crimeTypes = await _context.CrimeTypes.AsNoTracking()
            .Where(type => type.IsActive)
            .OrderBy(type => type.DisplayOrder)
            .ThenBy(type => type.Name)
            .Take(BulkImportLimits.MaxReferenceValues + 1)
            .Select(type => new { type.Code, type.Name })
            .ToListAsync(cancellationToken);

        var districts = await _context.Districts.AsNoTracking()
            .Where(district => district.IsActive)
            .OrderBy(district => district.Name)
            .Take(BulkImportLimits.MaxReferenceValues + 1)
            .Select(district => new { district.Code, district.Name })
            .ToListAsync(cancellationToken);

        var stations = await _context.PoliceStations.AsNoTracking()
            .Where(station => station.IsActive)
            .OrderBy(station => station.Name)
            .Take(BulkImportLimits.MaxReferenceValues + 1)
            .Select(station => new { station.Code, station.Name })
            .ToListAsync(cancellationToken);

        var ceiling = BulkImportLimits.MaxReferenceValues;

        return new BulkImportRequirementsDto
        {
            CrimeTypes = DescribeValues(crimeTypes.Select(item => ValueLabel(item.Code, item.Name)), ceiling),
            Districts = DescribeValues(districts.Select(item => ValueLabel(item.Code, item.Name)), ceiling),
            PoliceStations = DescribeValues(stations.Select(item => ValueLabel(item.Code, item.Name)), ceiling),
            ReferenceDataTruncated = crimeTypes.Count > ceiling
                                     || districts.Count > ceiling
                                     || stations.Count > ceiling
        };
    }

    public async Task<IReadOnlyList<BulkImportJobDto>> GetRecentJobsAsync(
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var jobs = await _context.ImportJobs.AsNoTracking()
            .Where(job => job.CreatedById == actorUserId)
            .OrderByDescending(job => job.CreatedAt)
            .ThenByDescending(job => job.Id)
            .Take(BulkImportLimits.MaxRecentJobs)
            .ToListAsync(cancellationToken);

        if (jobs.Count == 0)
        {
            return [];
        }

        // One extra query for the operator names instead of one per listed job.
        var creatorIds = jobs.Select(job => job.CreatedById).Distinct().ToList();

        var names = await _context.Set<ApplicationUser>().AsNoTracking()
            .Where(user => creatorIds.Contains(user.Id))
            .Select(user => new { user.Id, user.FirstName, user.LastName })
            .ToListAsync(cancellationToken);

        var byUser = names.ToDictionary(user => user.Id, user => $"{user.FirstName} {user.LastName}".Trim());

        return jobs
            .Select(job => ToDto(
                job,
                FullName(byUser, job.CreatedById),
                totals: null,
                canImport: false,
                awaitingValidation: job.Status == ImportStatus.Pending))
            .ToList();
    }

    // ----------------------------------------------------------------------------- upload

    public async Task<OperationResult<BulkImportJobDto>> UploadAsync(
        UploadBulkImportRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var error = ValidateUpload(request);

        if (error is null && request.Name.Trim().Length > BulkImportLimits.MaxJobNameLength)
        {
            error = $"The import name must be {BulkImportLimits.MaxJobNameLength} characters or fewer.";
        }

        if (error is null && request.Description?.Trim().Length > 4_000)
        {
            error = "The import description must be 4,000 characters or fewer.";
        }

        if (error is not null)
        {
            return OperationResult<BulkImportJobDto>.ValidationFailed(
                new Dictionary<string, string[]> { [nameof(request.OriginalFileName)] = [error] });
        }

        // CreatedById is a real foreign key, so the acting principal must exist.
        var actor = await _context.Set<ApplicationUser>().AsNoTracking()
            .FirstOrDefaultAsync(user => user.Id == actorUserId, cancellationToken);

        if (actor is null)
        {
            return OperationResult<BulkImportJobDto>.Forbidden("The signed-in account is not recognised.");
        }

        await using var content = request.Content.CanSeek
            ? request.Content
            : await CopyToMemoryAsync(request.Content, cancellationToken);

        var stored = await SaveUploadAsync(request, cancellationToken);

        if (stored is null)
        {
            return OperationResult<BulkImportJobDto>.ValidationFailed(
                new Dictionary<string, string[]>
                {
                    [nameof(request.OriginalFileName)] = ["The file could not be stored. Try again."]
                });
        }

        var now = _time.GetUtcNow().UtcDateTime;
        var label = DisplayName(request.OriginalFileName);

        var job = new ImportJob
        {
            Name = string.IsNullOrWhiteSpace(request.Name) ? label : request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            ImportType = ImportType.CrimeCases,
            Status = ImportStatus.Pending,
            SourceFilePath = stored.StoragePath,
            SourceFileSizeBytes = stored.SizeBytes,
            SourceFileContentType = request.ContentType,
            Configuration = JsonSerializer.Serialize(
                ImportJobConfiguration.Blank(label), JsonOptions),
            StartedAt = now,
            CreatedById = actorUserId,
            CreatedAt = now
        };

        _context.ImportJobs.Add(job);

        // The audit trail records the upload, never its contents: label, size and checksum.
        _audit.Record(
            AuditEntityTypeNames.ImportJob,
            job.Id,
            AuditActionNames.ImportUploaded,
            actorUserId,
            newValue: $"{label} ({stored.SizeBytes} bytes, sha256 {Checksum(stored.ChecksumSha256)})");

        await _context.SaveChangesAsync(cancellationToken);

        return OperationResult<BulkImportJobDto>.Success(
            ToDto(job, actor.FullName, totals: null, canImport: false, awaitingValidation: true));
    }

    public async Task<OperationResult<BulkImportJobDto>> GetJobAsync(
        Guid jobId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var staged = await LoadStagedJobAsync(jobId, actorUserId, cancellationToken);

        if (staged.Access != JobAccess.Available)
        {
            return Denial<BulkImportJobDto>(staged.Access);
        }

        return OperationResult<BulkImportJobDto>.Success(ToDto(
            staged.Job,
            staged.CreatedByName,
            totals: null,
            canImport: staged.Job.Status == ImportStatus.Validated && staged.Totals.Valid > 0,
            awaitingValidation: staged.Job.Status == ImportStatus.Pending));
    }

    // --------------------------------------------------------------------------- staging

    public async Task<OperationResult<BulkImportValidationDto>> ValidateAsync(
        Guid jobId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var staged = await LoadStagedJobAsync(jobId, actorUserId, cancellationToken);

        if (staged.Access != JobAccess.Available)
        {
            return Denial<BulkImportValidationDto>(staged.Access);
        }

        var job = staged.Job;

        if (job.Status != ImportStatus.Pending && job.Status != ImportStatus.Validated)
        {
            return OperationResult<BulkImportValidationDto>.ValidationFailed(new Dictionary<string, string[]>
            {
                [""] = [$"This import is {StatusWords(job.Status)} and can no longer be validated."]
            });
        }

        // Refreshing an already validated page must not re-stage or duplicate rows.
        if (job.Status == ImportStatus.Validated && staged.Totals.StagedRows > 0)
        {
            return OperationResult<BulkImportValidationDto>.Success(
                await BuildPersistedValidationAsync(job, staged, cancellationToken));
        }

        var plan = await ParseAndClassifyAsync(job, cancellationToken);

        if (!plan.Succeeded)
        {
            await MarkFailedAsync(job, plan.FileError!, actorUserId, cancellationToken);

            return OperationResult<BulkImportValidationDto>.ValidationFailed(
                new Dictionary<string, string[]> { ["File"] = [plan.FileError!] });
        }

        await ClearStagingAsync(job.Id, cancellationToken);

        var now = _time.GetUtcNow().UtcDateTime;
        var rawByRow = plan.Rows.ToDictionary(row => row.RowNumber, row => row.RawText);

        foreach (var row in plan.Rows)
        {
            var firstError = row.Issues.FirstOrDefault(issue => issue.Severity == ImportIssueSeverity.Error)
                             ?? row.Issues.FirstOrDefault();

            _context.ImportRecords.Add(new ImportRecord
            {
                ImportJobId = job.Id,
                RecordNumber = row.RowNumber,
                // ImportRecords.RawData is nvarchar(4000), the same budget the CSV reader was
                // given; SQL Server rejects anything longer with error 2628 and InMemory does not.
                RawData = Truncate(row.RawText, BulkImportLimits.MaxRawRowChars),
                Status = row.Status,
                ErrorMessage = firstError is null ? null : Truncate(firstError.Message, 2_000),
                ProcessedAt = now
            });
        }

        foreach (var issue in plan.Issues)
        {
            rawByRow.TryGetValue(issue.RowNumber, out var rawText);

            _context.ImportErrors.Add(new ImportError
            {
                ImportJobId = job.Id,
                RecordNumber = issue.RowNumber,
                ErrorCode = issue.Code,
                ErrorMessage = Truncate(issue.Message, 2_000),
                RawData = Truncate(rawText, BulkImportLimits.MaxRawRowChars),
                Severity = issue.Severity,
                OccurredAt = now
            });
        }

        job.TotalRecords = plan.TotalRows;
        job.ProcessedRecords = plan.Rows.Count;
        job.SuccessfulRecords = 0;
        job.FailedRecords = plan.Invalid + plan.Duplicate;
        job.CompletedAt = null;
        job.Status = ImportStatus.Validated;
        job.Configuration = JsonSerializer.Serialize(
            new ImportJobConfiguration(
                BulkImportSchema.CaseImportType,
                plan.HeaderNames.Length,
                plan.HeaderNames,
                plan.FileLabel,
                now),
            JsonOptions);

        _audit.Record(
            AuditEntityTypeNames.ImportJob,
            job.Id,
            AuditActionNames.ImportValidated,
            actorUserId,
            newValue: $"{plan.Valid} valid, {plan.Invalid} invalid, {plan.Duplicate} duplicate "
                      + $"of {plan.TotalRows} row(s); {plan.Warning} warning(s)");

        await _context.SaveChangesAsync(cancellationToken);

        var totals = RowTotals.From(plan);
        var issuesList = plan.Issues;

        return OperationResult<BulkImportValidationDto>.Success(new BulkImportValidationDto
        {
            Job = ToDto(job, staged.CreatedByName, totals, canImport: plan.Valid > 0, awaitingValidation: false),
            TotalRows = plan.TotalRows,
            ValidRows = plan.Valid,
            InvalidRows = plan.Invalid,
            DuplicateRows = plan.Duplicate,
            WarningRows = plan.Warning,
            Issues = issuesList.Take(BulkImportLimits.MaxShownIssues).ToList(),
            IssuesTruncated = issuesList.Count > BulkImportLimits.MaxShownIssues
                              || plan.DroppedIssues > 0,
            ProblemRows = plan.Rows
                .Where(row => row.Status is ImportRecordStatus.Invalid or ImportRecordStatus.Duplicate)
                .OrderBy(row => row.RowNumber)
                .Take(BulkImportLimits.MaxShownIssues)
                .Select(row => new BulkImportRowDto
                {
                    RowNumber = row.RowNumber,
                    Status = row.Status,
                    FirstIssue = row.Issues.FirstOrDefault()?.Message,
                    Issues = row.Issues.Select(issue => issue.Message).ToList()
                })
                .ToList(),
            ValidatedAt = now,
            SchemaName = BulkImportSchema.CaseImportType
        });
    }

    // --------------------------------------------------------------------------- preview

    public async Task<OperationResult<BulkImportPreviewDto>> PreviewAsync(
        Guid jobId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var staged = await LoadStagedJobAsync(jobId, actorUserId, cancellationToken);

        if (staged.Access != JobAccess.Available)
        {
            return Denial<BulkImportPreviewDto>(staged.Access);
        }

        var job = staged.Job;

        if (job.Status == ImportStatus.Pending)
        {
            return NeedsValidation<BulkImportPreviewDto>();
        }

        if (job.Status == ImportStatus.Processing)
        {
            return OperationResult<BulkImportPreviewDto>.ValidationFailed(new Dictionary<string, string[]>
            {
                [""] = ["This import is already running."]
            });
        }

        var configuration = ReadConfiguration(job.Configuration);
        var headerNames = configuration?.Header ?? [];

        var stagedRecords = await _context.ImportRecords.AsNoTracking()
            .Where(record => record.ImportJobId == job.Id)
            .OrderBy(record => record.RecordNumber)
            .ToListAsync(cancellationToken);

        var acceptedRows = stagedRecords
            .Where(record => record.Status is ImportRecordStatus.Valid or ImportRecordStatus.Imported)
            .ToList();

        var previewRows = acceptedRows
            .Take(BulkImportLimits.MaxPreviewRows)
            .Select(record => ToRowDto(record, headerNames))
            .ToList();

        // Looking at a preview imports nothing: the only write below is the audit entry.
        _audit.Record(
            AuditEntityTypeNames.ImportJob,
            job.Id,
            AuditActionNames.ImportPreviewed,
            actorUserId,
            newValue: $"{previewRows.Count} of {acceptedRows.Count} accepted row(s) shown");

        await _context.SaveChangesAsync(cancellationToken);

        var issues = await LoadIssuesAsync(job.Id, cancellationToken);
        var alreadyRun = DescribeAlreadyRun(job.Status);

        return OperationResult<BulkImportPreviewDto>.Success(new BulkImportPreviewDto
        {
            Job = ToDto(
                job,
                staged.CreatedByName,
                staged.Totals,
                canImport: job.Status == ImportStatus.Validated && acceptedRows.Count > 0,
                awaitingValidation: false,
                blockedBecause: alreadyRun ?? (acceptedRows.Count == 0
                    ? "No row passed validation, so there is nothing to import."
                    : null)),
            Rows = previewRows,
            ValidRows = acceptedRows.Count,
            InvalidRows = staged.Totals.Invalid,
            DuplicateRows = staged.Totals.Duplicate,
            WarningRows = staged.Totals.Warning,
            TotalRows = stagedRecords.Count,
            MoreRowsBeyondPreview = acceptedRows.Count > previewRows.Count,
            Issues = issues,
            IssuesTruncated = issues.Count >= BulkImportLimits.MaxShownIssues
                              && staged.Totals.IssueCount > issues.Count,
            SchemaName = configuration?.Schema ?? BulkImportSchema.CaseImportType
        });
    }

    // ------------------------------------------------------------------- confirmed import

    public async Task<OperationResult<BulkImportResultDto>> ImportAsync(
        Guid jobId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var staged = await LoadStagedJobAsync(jobId, actorUserId, cancellationToken);

        if (staged.Access != JobAccess.Available)
        {
            return Denial<BulkImportResultDto>(staged.Access);
        }

        var job = staged.Job;

        if (job.Status != ImportStatus.Validated)
        {
            return OperationResult<BulkImportResultDto>.Conflict(
                string.Empty,
                job.Status == ImportStatus.Pending
                    ? "Validate the file before importing it."
                    : $"This import is {StatusWords(job.Status)}, so it cannot be run again.");
        }

        if (staged.Totals.Valid == 0)
        {
            return OperationResult<BulkImportResultDto>.ValidationFailed(new Dictionary<string, string[]>
            {
                [""] = ["No row passed validation, so there is nothing to import."]
            });
        }

        // The claim commits on its own, before any case is written. Once a job is Processing,
        // a second submission of the same job is refused instead of importing it twice.
        job.Status = ImportStatus.Processing;
        job.StartedAt = _time.GetUtcNow().UtcDateTime;

        _audit.Record(
            AuditEntityTypeNames.ImportJob,
            job.Id,
            AuditActionNames.ImportStarted,
            actorUserId,
            newValue: $"{job.Name} ({staged.Totals.Valid} accepted row(s))");

        await _context.SaveChangesAsync(cancellationToken);

        // The in-memory provider used by the test suite has no transactions, so one is opened
        // only where the provider supports it. Atomicity on every provider comes from writing
        // the whole accepted set in the single SaveChangesAsync below.
        IDbContextTransaction? transaction = null;

        if (_context.Database.IsRelational())
        {
            transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            var result = await WriteCasesAsync(job, staged, actorUserId, cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return result;
        }
        catch (BulkImportAbortedException abort)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            await MarkFailedAsync(job, abort.Message, actorUserId, cancellationToken);

            return OperationResult<BulkImportResultDto>.Conflict(
                string.Empty,
                $"{abort.Message} Nothing was imported and no cases were created.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            // The exception itself stays out of the response: only its type is audited.
            await MarkFailedAsync(
                job,
                "The import could not be completed.",
                actorUserId,
                cancellationToken,
                detail: exception.GetType().Name);

            return OperationResult<BulkImportResultDto>.ValidationFailed(new Dictionary<string, string[]>
            {
                [""] = ["The import could not be completed. Nothing was imported and no cases were created."]
            });
        }
        finally
        {
            transaction?.Dispose();
        }
    }

    /// <summary>
    /// Re-reads and re-validates the stored file, builds the accepted rows, checks the case
    /// numbers against the register one final time and writes everything in one commit.
    /// </summary>
    private async Task<OperationResult<BulkImportResultDto>> WriteCasesAsync(
        ImportJob job,
        StagedJob staged,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var plan = await ParseAndClassifyAsync(job, cancellationToken);

        if (!plan.Succeeded)
        {
            throw new BulkImportAbortedException(
                plan.FileError ?? "The stored file could not be read again.");
        }

        // Re-check of the import state: the file must still produce the outcome the operator
        // confirmed. A file that no longer matches is refused rather than partially applied.
        if (plan.TotalRows != job.TotalRecords
            || plan.Invalid + plan.Duplicate != job.FailedRecords)
        {
            throw new BulkImportAbortedException(
                "The stored file no longer matches its validated result.");
        }

        var accepted = plan.Rows
            .Where(row => row.Status == ImportRecordStatus.Valid && row.NewCase is not null)
            .OrderBy(row => row.RowNumber)
            .ToList();

        if (accepted.Count == 0)
        {
            throw new BulkImportAbortedException("No row passed validation.");
        }

        var numbers = accepted
            .Select(row => row.NewCase!.CaseNumber!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var clashes = await FindExistingCaseNumbersAsync(numbers, cancellationToken);

        if (clashes.Count > 0)
        {
            // A case appeared in the register between validation and confirmation.
            throw new BulkImportAbortedException(
                $"Case number(s) already exist in NETRA: {string.Join(", ", clashes.Take(5))}.");
        }

        var stagedRecords = await _context.ImportRecords
            .Where(record => record.ImportJobId == job.Id)
            .ToListAsync(cancellationToken);

        var byRowNumber = stagedRecords.ToDictionary(record => record.RecordNumber);

        var now = _time.GetUtcNow().UtcDateTime;
        var created = new List<CreatedCase>(accepted.Count);

        foreach (var row in accepted)
        {
            var draft = row.NewCase!;

            var crimeCase = new CrimeCase
            {
                CaseNumber = draft.CaseNumber!,
                Title = draft.Title!,
                Description = draft.Description,
                CrimeTypeId = draft.CrimeTypeId!.Value,
                Status = draft.Status,
                Severity = draft.Severity,
                IncidentDate = draft.IncidentDate!.Value,
                ReportedDate = now,
                PoliceStationId = draft.PoliceStationId,
                CreatedById = job.CreatedById,
                CreatedAt = now,
                UpdatedAt = now
            };

            _context.CrimeCases.Add(crimeCase);

            // Scene data follows the case register's own shape: the location row and its
            // CaseLocation link are only created when the row carries a district.
            if (draft.DistrictId is { } districtId)
            {
                var location = new NETRA.Domain.Geography.Location
                {
                    Name = string.IsNullOrWhiteSpace(draft.Address)
                        ? "Incident location"
                        : Truncate(draft.Address.Trim(), 200)!,
                    Type = "Incident",
                    Address = draft.Address?.Trim(),
                    Latitude = draft.Latitude,
                    Longitude = draft.Longitude,
                    DistrictId = districtId,
                    PoliceStationId = draft.PoliceStationId,
                    CreatedAt = now
                };

                _context.Locations.Add(location);

                _context.CaseLocations.Add(new CaseLocation
                {
                    CrimeCase = crimeCase,
                    Location = location,
                    LocationRole = IncidentLocationRole,
                    AssociatedAt = now
                });
            }

            _context.Investigations.Add(new Investigation
            {
                CrimeCase = crimeCase,
                Title = $"Investigation \u2014 {crimeCase.CaseNumber}",
                StartedAt = now,
                LeadInvestigatorId = job.CreatedById,
                Status = 1,
                CreatedAt = now,
                UpdatedAt = now
            });

            _audit.Record(
                AuditEntityTypeNames.CrimeCase,
                crimeCase.Id,
                AuditActionNames.Created,
                actorUserId,
                newValue: $"Case {crimeCase.CaseNumber} registered by bulk import");

            if (byRowNumber.TryGetValue(row.RowNumber, out var record))
            {
                record.Status = ImportRecordStatus.Imported;
                record.CreatedEntityType = CreatedEntityType;
                record.ProcessedAt = now;
            }

            created.Add(new CreatedCase(row.RowNumber, crimeCase));
        }

        job.TotalRecords = plan.TotalRows;
        job.ProcessedRecords = plan.Rows.Count;
        job.SuccessfulRecords = created.Count;
        job.FailedRecords = plan.Invalid + plan.Duplicate;
        job.Status = plan.Invalid + plan.Duplicate > 0
            ? ImportStatus.CompletedWithErrors
            : ImportStatus.Completed;
        job.CompletedAt = now;

        _audit.Record(
            AuditEntityTypeNames.ImportJob,
            job.Id,
            AuditActionNames.ImportCompleted,
            actorUserId,
            newValue: $"{created.Count} case(s) created, {job.FailedRecords} row(s) not imported");

        await _context.SaveChangesAsync(cancellationToken);

        // The new cases have identities only after the commit above, so the staged rows are
        // linked to them in the same transaction.
        foreach (var entry in created)
        {
            if (byRowNumber.TryGetValue(entry.RowNumber, out var record))
            {
                record.CreatedEntityId = entry.Case.Id;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        return OperationResult<BulkImportResultDto>.Success(new BulkImportResultDto
        {
            Job = ToDto(job, staged.CreatedByName, RowTotals.From(plan),
                canImport: false, awaitingValidation: false),
            ImportedRows = created.Count,
            SkippedDuplicateRows = plan.Duplicate,
            RejectedRows = plan.Invalid,
            TotalRows = plan.TotalRows,
            ImportedCases = created
                .Take(BulkImportLimits.MaxPreviewRows)
                .Select(entry => new BulkImportImportedCaseDto
                {
                    CaseId = entry.Case.Id,
                    RowNumber = entry.RowNumber,
                    CaseNumber = entry.Case.CaseNumber,
                    Title = entry.Case.Title
                })
                .ToList(),
            MoreCasesThanShown = created.Count > BulkImportLimits.MaxPreviewRows,
            Issues = await LoadIssuesAsync(job.Id, cancellationToken),
            IssuesTruncated = false,
            CompletedAt = now,
            Succeeded = true
        });
    }

    // ------------------------------------------------------------------ job housekeeping

    public async Task<OperationResult<BulkImportJobDto>> CancelAsync(
        Guid jobId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var staged = await LoadStagedJobAsync(jobId, actorUserId, cancellationToken);

        if (staged.Access != JobAccess.Available)
        {
            return Denial<BulkImportJobDto>(staged.Access);
        }

        var job = staged.Job;

        if (job.Status is not (ImportStatus.Pending or ImportStatus.Validated))
        {
            return OperationResult<BulkImportJobDto>.Conflict(
                string.Empty,
                $"This import is {StatusWords(job.Status)}, so it cannot be cancelled.");
        }

        job.Status = ImportStatus.Cancelled;
        job.CompletedAt = _time.GetUtcNow().UtcDateTime;

        _audit.Record(
            AuditEntityTypeNames.ImportJob,
            job.Id,
            AuditActionNames.ImportCancelled,
            actorUserId,
            newValue: $"{job.Name} cancelled before importing");

        await _context.SaveChangesAsync(cancellationToken);

        return OperationResult<BulkImportJobDto>.Success(
            ToDto(job, staged.CreatedByName, staged.Totals, canImport: false, awaitingValidation: false));
    }

    public async Task<OperationResult<BulkImportResultDto>> GetResultAsync(
        Guid jobId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var staged = await LoadStagedJobAsync(jobId, actorUserId, cancellationToken);

        if (staged.Access != JobAccess.Available)
        {
            return Denial<BulkImportResultDto>(staged.Access);
        }

        var job = staged.Job;

        var linkedRows = await _context.ImportRecords.AsNoTracking()
            .Where(record => record.ImportJobId == job.Id
                             && record.Status == ImportRecordStatus.Imported
                             && record.CreatedEntityId != null)
            .OrderBy(record => record.RecordNumber)
            .Select(record => new { record.RecordNumber, CaseId = record.CreatedEntityId!.Value })
            .ToListAsync(cancellationToken);

        var shownIds = linkedRows.Select(row => row.CaseId).Take(BulkImportLimits.MaxPreviewRows).ToList();

        var cases = shownIds.Count == 0
            ? []
            : await _context.CrimeCases.AsNoTracking()
                .Where(crimeCase => shownIds.Contains(crimeCase.Id))
                .Select(crimeCase => new { crimeCase.Id, crimeCase.CaseNumber, crimeCase.Title })
                .ToListAsync(cancellationToken);

        var byId = cases.ToDictionary(item => item.Id);

        return OperationResult<BulkImportResultDto>.Success(new BulkImportResultDto
        {
            Job = ToDto(job, staged.CreatedByName, staged.Totals,
                canImport: false, awaitingValidation: false),
            ImportedRows = linkedRows.Count,
            SkippedDuplicateRows = staged.Totals.Duplicate,
            RejectedRows = staged.Totals.Invalid,
            TotalRows = staged.Totals.StagedRows,
            ImportedCases = linkedRows
                .Where(row => byId.ContainsKey(row.CaseId))
                .Take(BulkImportLimits.MaxPreviewRows)
                .Select(row => new BulkImportImportedCaseDto
                {
                    CaseId = row.CaseId,
                    RowNumber = row.RecordNumber,
                    CaseNumber = byId[row.CaseId].CaseNumber,
                    Title = byId[row.CaseId].Title
                })
                .ToList(),
            MoreCasesThanShown = linkedRows.Count > BulkImportLimits.MaxPreviewRows,
            Issues = await LoadIssuesAsync(job.Id, cancellationToken),
            IssuesTruncated = staged.Totals.IssueCount > BulkImportLimits.MaxShownIssues,
            CompletedAt = job.CompletedAt,
            Succeeded = job.Status is ImportStatus.Completed or ImportStatus.CompletedWithErrors
        });
    }

    // --------------------------------------------------------------------- parse + rules

    /// <summary>
    /// Reads the stored file and classifies every row. Performs no writes at all, so it is
    /// safe to run during validation and again inside the import.
    /// </summary>
    private async Task<ImportPlan> ParseAndClassifyAsync(ImportJob job, CancellationToken cancellationToken)
    {
        await using var content = await _storage.OpenReadAsync(job.SourceFilePath, cancellationToken);

        if (content is null)
        {
            return ImportPlan.Fail("The stored file could not be read. Upload the file again.");
        }

        if (content.Length == 0)
        {
            return ImportPlan.Fail("The stored file is empty.");
        }

        if (content.Length > BulkImportLimits.MaxFileBytes)
        {
            return ImportPlan.Fail(
                $"The stored file is larger than the {Megabytes(BulkImportLimits.MaxFileBytes)} import limit.");
        }

        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var read = CsvReader.Read(
            reader,
            new CsvReadLimits
            {
                MaxRecords = BulkImportLimits.MaxDataRows + 1,
                MaxRawChars = BulkImportLimits.MaxRawRowChars
            });

        if (read.LimitExceeded)
        {
            return ImportPlan.Fail(
                $"The file holds more than the {BulkImportLimits.MaxDataRows:N0} data rows one import accepts.");
        }

        if (!read.Succeeded)
        {
            return ImportPlan.Fail(read.Error!);
        }

        if (read.Records.Count == 0)
        {
            return ImportPlan.Fail("The file contains no rows at all.");
        }

        if (!TryMapHeader(read.Records[0], out var headerNames, out var headerIndex, out var headerError))
        {
            return ImportPlan.Fail(headerError!);
        }

        var dataRecords = read.Records.Skip(1).ToList();

        if (dataRecords.Count == 0)
        {
            return ImportPlan.Fail("The file has a header row but no data rows.");
        }

        var caseNumberColumn = ColumnHeaderIndex(headerIndex, "CaseNumber");

        var candidates = caseNumberColumn < 0
            ? []
            : dataRecords
                .Select(record => record.Fields.Count > caseNumberColumn
                    ? record.Fields[caseNumberColumn].Trim()
                    : string.Empty)
                .Where(number => number.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        var references = await LoadReferencesAsync(candidates, cancellationToken);

        var plan = new ImportPlan
        {
            HeaderNames = headerNames,
            TotalRows = dataRecords.Count,
            FileLabel = ReadConfiguration(job.Configuration)?.FileName
        };

        var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var position = 0; position < dataRecords.Count; position++)
        {
            plan.Rows.Add(ClassifyRow(
                dataRecords[position],
                position + 1,
                headerNames,
                headerIndex,
                references,
                seenInFile,
                plan));

            cancellationToken.ThrowIfCancellationRequested();
        }

        plan.ReduceCounts();
        return plan;
    }

    private static int ColumnHeaderIndex(Dictionary<string, int> headerIndex, string columnName) =>
        headerIndex.TryGetValue(columnName, out var index) ? index : -1;

    /// <summary>
    /// Maps the file's own column order onto the schema. Column names are matched
    /// case-insensitively; a missing required column, a repeated column or an unknown column
    /// makes the whole file unusable rather than silently dropping data.
    /// </summary>
    private static bool TryMapHeader(
        CsvRecord header,
        out string[] headerNames,
        out Dictionary<string, int> headerIndex,
        out string? error)
    {
        headerIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        error = null;

        headerNames = header.Fields.Select(field => field.Trim()).ToArray();

        if (headerNames.All(string.IsNullOrEmpty))
        {
            headerNames = [];
            error = "The first row must name the columns NETRA should import.";
            return false;
        }

        if (header.Fields.Any(ContainsControlCharacters))
        {
            headerNames = [];
            error = "The file contains control characters and cannot be read as CSV.";
            return false;
        }

        for (var index = 0; index < headerNames.Length; index++)
        {
            var name = headerNames[index];

            if (name.Length == 0)
            {
                headerNames = [];
                error = "Every column in the header row must be named.";
                return false;
            }

            if (!headerIndex.TryAdd(name, index))
            {
                headerNames = [];
                error = $"The header row repeats the column '{name}'.";
                return false;
            }

            if (BulkImportSchema.Find(name) is null)
            {
                headerNames = [];
                error = $"NETRA does not import the column '{name}'. "
                        + $"Supported columns are {string.Join(", ", BulkImportSchema.AllNames)}.";
                return false;
            }
        }

        var missing = new List<string>();

        foreach (var required in BulkImportSchema.RequiredNames)
        {
            if (!headerIndex.ContainsKey(required))
            {
                missing.Add(required);
            }
        }

        if (missing.Count > 0)
        {
            headerNames = [];
            error = $"The header row is missing required column(s): {string.Join(", ", missing)}.";
            return false;
        }

        return true;
    }

    private RowPlan ClassifyRow(
        CsvRecord record,
        int rowNumber,
        string[] headerNames,
        Dictionary<string, int> headerIndex,
        ReferenceData references,
        HashSet<string> seenInFile,
        ImportPlan plan)
    {
        var row = new RowPlan
        {
            RowNumber = rowNumber,
            RawText = record.Raw.Length == 0 ? CsvReader.WriteRecord(record.Fields) : record.Raw
        };

        string? Value(string columnName)
        {
            var index = ColumnHeaderIndex(headerIndex, columnName);
            return index < 0 || index >= record.Fields.Count ? null : record.Fields[index].Trim();
        }

        void Issue(string field, string rule, string message,
            ImportIssueSeverity severity = ImportIssueSeverity.Error)
        {
            var issue = new BulkImportIssueDto
            {
                RowNumber = rowNumber,
                Field = field,
                Code = $"{field}.{rule}",
                Message = message,
                Severity = severity
            };

            row.Issues.Add(issue);
            plan.AddIssue(issue);
        }

        static bool Missing(string? value) => string.IsNullOrWhiteSpace(value);

        // A row that does not line up with the header cannot be interpreted safely.
        if (record.Fields.Count != headerNames.Length)
        {
            Issue(BulkImportIssueCodes.RowField, BulkImportIssueCodes.MalformedRow,
                $"Row {rowNumber} has {record.Fields.Count} value(s) but the header names "
                + $"{headerNames.Length} column(s), so the row was rejected.");

            row.Status = ImportRecordStatus.Invalid;
            return row;
        }

        if (record.Fields.All(string.IsNullOrWhiteSpace))
        {
            Issue(BulkImportIssueCodes.RowField, BulkImportIssueCodes.EmptyRow,
                $"Row {rowNumber} is empty.");

            row.Status = ImportRecordStatus.Invalid;
            return row;
        }

        if (record.Fields.Any(ContainsControlCharacters))
        {
            Issue(BulkImportIssueCodes.RowField, BulkImportIssueCodes.ControlCharacter,
                $"Row {rowNumber} contains control characters and was rejected.");

            row.Status = ImportRecordStatus.Invalid;
            return row;
        }

        foreach (var column in BulkImportSchema.Columns)
        {
            var value = Value(column.Name);

            if (string.IsNullOrWhiteSpace(value))
            {
                if (column.Required)
                {
                    Issue(column.Name, BulkImportIssueCodes.RequiredValueMissing,
                        $"{column.Name} is required on row {rowNumber}.");
                }

                continue;
            }

            if (value.Length > column.MaxLength)
            {
                // Only the length is reported; free-text content is not repeated back.
                Issue(column.Name, BulkImportIssueCodes.ValueTooLong,
                    $"{column.Name} on row {rowNumber} is longer than {column.MaxLength:N0} characters.");
            }
        }

        var draft = new CaseDraft();
        var caseNumber = Value("CaseNumber");

        if (!Missing(caseNumber) && caseNumber!.Length <= 50)
        {
            draft.CaseNumber = caseNumber;

            if (!seenInFile.Add(caseNumber))
            {
                Issue("CaseNumber", BulkImportIssueCodes.DuplicateInFile,
                    $"Row {rowNumber} repeats case number '{caseNumber}' from earlier in the same file, "
                    + "so the row was skipped.");

                row.Status = ImportRecordStatus.Duplicate;
            }
            else if (references.ExistingCaseNumbers.Contains(caseNumber))
            {
                Issue("CaseNumber", BulkImportIssueCodes.CaseNumberExists,
                    $"Case number '{caseNumber}' already exists in NETRA, so row {rowNumber} was skipped.");

                row.Status = ImportRecordStatus.Duplicate;
            }
        }

        var crimeType = Value("CrimeType");

        if (!Missing(crimeType))
        {
            if (references.CrimeTypes.TryGetValue(crimeType!, out var resolvedType) && resolvedType.IsActive)
            {
                draft.CrimeTypeId = resolvedType.Id;
                draft.CrimeTypeName = resolvedType.Name;
            }
            else
            {
                Issue("CrimeType", BulkImportIssueCodes.UnknownCrimeType,
                    references.CrimeTypes.ContainsKey(crimeType!)
                        ? $"The crime type '{crimeType}' on row {rowNumber} is retired, so the row was rejected."
                        : $"No active crime type matches '{crimeType}' on row {rowNumber}.");
            }
        }

        var incidentDate = Value("IncidentDate");

        if (!Missing(incidentDate))
        {
            if (DateTime.TryParseExact(
                    incidentDate, AcceptedDateFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                if (parsed > _time.GetUtcNow().UtcDateTime.AddDays(1))
                {
                    Issue("IncidentDate", BulkImportIssueCodes.DateInFuture,
                        $"The incident date '{incidentDate}' on row {rowNumber} is in the future.");
                }
                else
                {
                    draft.IncidentDate = parsed;
                }
            }
            else
            {
                Issue("IncidentDate", BulkImportIssueCodes.InvalidDateFormat,
                    $"The incident date '{incidentDate}' on row {rowNumber} is not an ISO date (yyyy-MM-dd).");
            }
        }

        var status = Value("Status");

        if (!Missing(status))
        {
            if (Enum.TryParse<CaseStatus>(status, ignoreCase: true, out var parsedStatus)
                && Enum.IsDefined(parsedStatus))
            {
                draft.Status = parsedStatus;
            }
            else
            {
                Issue("Status", BulkImportIssueCodes.InvalidStatus,
                    $"The status '{status}' on row {rowNumber} is not a NETRA case status.");
            }
        }

        var severity = Value("Severity");

        if (!Missing(severity))
        {
            if (Enum.TryParse<CaseSeverity>(severity, ignoreCase: true, out var parsedSeverity)
                && Enum.IsDefined(parsedSeverity))
            {
                draft.Severity = parsedSeverity;
            }
            else
            {
                Issue("Severity", BulkImportIssueCodes.InvalidSeverity,
                    $"The severity '{severity}' on row {rowNumber} is not a NETRA case severity.");
            }
        }

        draft.Title = Missing(Value("Title")) ? null : Value("Title");
        draft.Description = Missing(Value("Description")) ? null : Value("Description");
        draft.Address = Missing(Value("Address")) ? null : Value("Address");

        Guid? districtId = null;
        var district = Value("District");

        if (!Missing(district))
        {
            if (references.Districts.TryGetValue(district!, out var resolvedDistrict) && resolvedDistrict.IsActive)
            {
                districtId = resolvedDistrict.Id;
                draft.DistrictId = resolvedDistrict.Id;
                draft.DistrictName = resolvedDistrict.Name;
            }
            else
            {
                Issue("District", BulkImportIssueCodes.UnknownDistrict,
                    references.Districts.ContainsKey(district!)
                        ? $"The district '{district}' on row {rowNumber} is retired, so the row was rejected."
                        : $"No active district matches '{district}' on row {rowNumber}.");
            }
        }

        var station = Value("PoliceStation");

        if (!Missing(station))
        {
            if (references.Stations.TryGetValue(station!, out var resolvedStation) && resolvedStation.IsActive)
            {
                if (districtId is { } expectedDistrict && resolvedStation.DistrictId != expectedDistrict)
                {
                    Issue("PoliceStation", BulkImportIssueCodes.StationDistrictMismatch,
                        $"The police station '{station}' on row {rowNumber} does not belong to the district "
                        + "named in the same row.");
                }
                else
                {
                    draft.PoliceStationId = resolvedStation.Id;
                    draft.PoliceStationName = resolvedStation.Name;
                }
            }
            else
            {
                Issue("PoliceStation", BulkImportIssueCodes.UnknownPoliceStation,
                    references.Stations.ContainsKey(station!)
                        ? $"The police station '{station}' on row {rowNumber} is retired, so the row was rejected."
                        : $"No active police station matches '{station}' on row {rowNumber}.");
            }
        }

        var latitude = Value("Latitude");
        var longitude = Value("Longitude");
        var parsedLatitude = ParseCoordinate(latitude, -90, 90);
        var parsedLongitude = ParseCoordinate(longitude, -180, 180);

        if (!Missing(latitude) && parsedLatitude is null)
        {
            Issue("Latitude", BulkImportIssueCodes.InvalidCoordinates,
                $"The latitude '{latitude}' on row {rowNumber} is not a number between -90 and 90.");
        }

        if (!Missing(longitude) && parsedLongitude is null)
        {
            Issue("Longitude", BulkImportIssueCodes.InvalidCoordinates,
                $"The longitude '{longitude}' on row {rowNumber} is not a number between -180 and 180.");
        }

        if (Missing(latitude) != Missing(longitude))
        {
            Issue("Latitude", BulkImportIssueCodes.CoordinatesIncomplete,
                $"Row {rowNumber} supplies only one coordinate; latitude and longitude must be supplied together.");
        }

        draft.Latitude = parsedLatitude;
        draft.Longitude = parsedLongitude;

        // Scene data reaches the register through the case-location link, which needs a district.
        if (districtId is null && (draft.Address is not null || parsedLatitude is not null || parsedLongitude is not null))
        {
            Issue("District", BulkImportIssueCodes.LocationWithoutDistrict,
                $"Row {rowNumber} supplies scene details without a district, so no incident location will be created.",
                ImportIssueSeverity.Warning);
        }

        var blocked = row.Issues.Any(issue => issue.Severity == ImportIssueSeverity.Error);

        var resolvable = draft.CaseNumber is not null
                         && draft.Title is not null
                         && draft.CrimeTypeId is not null
                         && draft.IncidentDate is not null;

        if (row.Status == ImportRecordStatus.Pending)
        {
            row.Status = blocked || !resolvable ? ImportRecordStatus.Invalid : ImportRecordStatus.Valid;
        }

        if (row.Status == ImportRecordStatus.Valid)
        {
            row.NewCase = draft;
        }

        return row;
    }

    // ----------------------------------------------------------------------- plan types

    /// <summary>Everything one pass over the file learned, with counts already reduced.</summary>
    private sealed class ImportPlan
    {
        public List<RowPlan> Rows { get; } = [];

        public List<BulkImportIssueDto> Issues { get; } = [];

        public string[] HeaderNames { get; init; } = [];

        /// <summary>Display name of the uploaded file, carried into the job configuration.</summary>
        public string? FileLabel { get; init; }

        public int TotalRows { get; init; }

        public int Valid { get; private set; }

        public int Invalid { get; private set; }

        public int Duplicate { get; private set; }

        public int Warning { get; private set; }

        public int DroppedIssues { get; private set; }

        public bool Succeeded { get; private init; } = true;

        public string? FileError { get; private init; }

        /// <summary>Issue text is capped by the limits; the count of what was dropped is not.</summary>
        public void AddIssue(BulkImportIssueDto issue)
        {
            if (Issues.Count >= BulkImportLimits.MaxStoredIssues)
            {
                DroppedIssues++;
                return;
            }

            Issues.Add(issue);
        }

        public void ReduceCounts()
        {
            foreach (var row in Rows)
            {
                switch (row.Status)
                {
                    case ImportRecordStatus.Valid:
                        Valid++;
                        break;
                    case ImportRecordStatus.Invalid:
                        Invalid++;
                        break;
                    case ImportRecordStatus.Duplicate:
                        Duplicate++;
                        break;
                }

                if (row.Issues.Any(issue => issue.Severity == ImportIssueSeverity.Warning))
                {
                    Warning++;
                }
            }
        }

        public static ImportPlan Fail(string error) => new() { Succeeded = false, FileError = error };
    }

    private sealed class RowPlan
    {
        public int RowNumber { get; init; }

        public string RawText { get; init; } = string.Empty;

        public ImportRecordStatus Status { get; set; } = ImportRecordStatus.Pending;

        public List<BulkImportIssueDto> Issues { get; } = [];

        public CaseDraft? NewCase { get; set; }
    }

    /// <summary>A row resolved against NETRA's own reference data, ready to become a case.</summary>
    private sealed class CaseDraft
    {
        public string? CaseNumber { get; set; }

        public string? Title { get; set; }

        public Guid? CrimeTypeId { get; set; }

        public string? CrimeTypeName { get; set; }

        public DateTime? IncidentDate { get; set; }

        public CaseStatus Status { get; set; } = CaseStatus.Open;

        public CaseSeverity Severity { get; set; } = CaseSeverity.Medium;

        public string? Description { get; set; }

        public Guid? DistrictId { get; set; }

        public string? DistrictName { get; set; }

        public Guid? PoliceStationId { get; set; }

        public string? PoliceStationName { get; set; }

        public string? Address { get; set; }

        public double? Latitude { get; set; }

        public double? Longitude { get; set; }
    }

    private sealed record CreatedCase(int RowNumber, CrimeCase Case);

    private sealed class ReferenceData
    {
        public Dictionary<string, CrimeType> CrimeTypes { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, District> Districts { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, PoliceStation> Stations { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> ExistingCaseNumbers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private enum JobAccess
    {
        Available,
        Missing,
        NotOwner
    }

    private sealed class StagedJob
    {
        public required ImportJob Job { get; init; }

        public required string CreatedByName { get; init; }

        public JobAccess Access { get; init; }

        public RowTotals Totals { get; init; } = RowTotals.Empty;
    }

    private sealed record RowTotals(
        int StagedRows,
        int Valid,
        int Invalid,
        int Duplicate,
        int Imported,
        int Warning,
        int IssueCount)
    {
        public static RowTotals Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);

        public static RowTotals From(ImportPlan plan) => new(
            plan.Rows.Count,
            plan.Valid,
            plan.Invalid,
            plan.Duplicate,
            Imported: 0,
            plan.Warning,
            plan.Issues.Count + plan.DroppedIssues);
    }

    /// <summary>Raised for expected import aborts whose message is safe to show the operator.</summary>
    private sealed class BulkImportAbortedException(string message) : Exception(message);

    /// <summary>
    /// Compact job metadata held in the existing <c>Configuration</c> column: the schema name,
    /// the column order needed to rebuild a preview and the upload's display label. No row
    /// content is stored here, which is why no schema change was needed for this module.
    /// </summary>
    private sealed record ImportJobConfiguration(
        string Schema,
        int Columns,
        string[] Header,
        string? FileName,
        DateTime? StagedAt)
    {
        public static ImportJobConfiguration Blank(string fileName) =>
            new(BulkImportSchema.CaseImportType, 0, [], fileName, null);
    }

    // --------------------------------------------------------------------------- helpers

    private async Task<StoredFileResult?> SaveUploadAsync(
        UploadBulkImportRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            // Storage generates the on-disk name, confines the write to its configured root
            // and hashes the bytes. The client name is only ever a label.
            return await _storage.SaveAsync(
                request.Content,
                request.OriginalFileName,
                request.ContentType,
                StorageFolder,
                cancellationToken);
        }
        catch (FileStorageException)
        {
            return null;
        }
    }

    private async Task<ReferenceData> LoadReferencesAsync(
        IReadOnlyList<string> candidateCaseNumbers,
        CancellationToken cancellationToken)
    {
        var crimeTypes = await _context.CrimeTypes.AsNoTracking()
            .OrderBy(type => type.DisplayOrder)
            .ThenBy(type => type.Name)
            .Take(MaxReferenceRows)
            .ToListAsync(cancellationToken);

        var districts = await _context.Districts.AsNoTracking()
            .OrderBy(district => district.Name)
            .Take(MaxReferenceRows)
            .ToListAsync(cancellationToken);

        var stations = await _context.PoliceStations.AsNoTracking()
            .OrderBy(station => station.Name)
            .Take(MaxReferenceRows)
            .ToListAsync(cancellationToken);

        return new ReferenceData
        {
            CrimeTypes = BuildIndex(
                crimeTypes,
                type => (type.Code, type.Name),
                type => type.IsActive),
            Districts = BuildIndex(
                districts,
                district => (district.Code, district.Name),
                district => district.IsActive),
            Stations = BuildIndex(
                stations,
                station => (station.Code, station.Name),
                station => station.IsActive),
            ExistingCaseNumbers = await FindExistingCaseNumbersAsync(
                candidateCaseNumbers, cancellationToken)
        };
    }

    /// <summary>
    /// Indexes reference rows by both code and name. An active row always wins a key over a
    /// retired one that shares it, so a retired code can never shadow a live value.
    /// </summary>
    private static Dictionary<string, T> BuildIndex<T>(
        IReadOnlyList<T> rows,
        Func<T, (string Code, string Name)> keys,
        Func<T, bool> isActive)
        where T : class
    {
        var index = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows.Where(isActive))
        {
            AddKey(index, keys(row).Code, row);
            AddKey(index, keys(row).Name, row);
        }

        foreach (var row in rows.Where(row => !isActive(row)))
        {
            // A retired row may only fill a key nothing active claims.
            AddKeyIfFree(index, keys(row).Code, row);
            AddKeyIfFree(index, keys(row).Name, row);
        }

        return index;
    }

    private static void AddKey<T>(Dictionary<string, T> index, string? key, T row)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            index[key.Trim()] = row;
        }
    }

    private static void AddKeyIfFree<T>(Dictionary<string, T> index, string? key, T row)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            index.TryAdd(key.Trim(), row);
        }
    }

    /// <summary>
    /// Case numbers already in the register, compared case-insensitively so the outcome does
    /// not depend on the database collation. Read in chunks instead of row by row.
    /// </summary>
    private async Task<HashSet<string>> FindExistingCaseNumbersAsync(
        IReadOnlyList<string> candidates,
        CancellationToken cancellationToken)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var start in ChunkStarts(candidates.Count, CaseNumberChunkSize))
        {
            var chunk = candidates
                .Skip(start)
                .Take(CaseNumberChunkSize)
                .Select(number => number.ToUpperInvariant())
                .ToList();

            if (chunk.Count == 0)
            {
                continue;
            }

            var matches = await _context.CrimeCases.AsNoTracking()
                .Where(crimeCase => chunk.Contains(crimeCase.CaseNumber.ToUpper()))
                .Select(crimeCase => crimeCase.CaseNumber)
                .ToListAsync(cancellationToken);

            foreach (var match in matches)
            {
                found.Add(match);
            }
        }

        return found;
    }

    private static IEnumerable<int> ChunkStarts(int total, int chunkSize)
    {
        for (var start = 0; start < total; start += chunkSize)
        {
            yield return start;
        }
    }

    private static double? ParseCoordinate(string? value, double minimum, double maximum)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            || double.IsNaN(parsed)
            || double.IsInfinity(parsed)
            || parsed < minimum
            || parsed > maximum)
        {
            return null;
        }

        return parsed;
    }

    private static bool ContainsControlCharacters(string value) =>
        value.Any(ch => char.IsControl(ch) && ch != '\t');

    private static string? ValidateUpload(UploadBulkImportRequest request)
    {
        var fileName = request.OriginalFileName.Trim();

        if (string.IsNullOrEmpty(fileName) || request.Length <= 0)
        {
            return "Choose a CSV file to upload.";
        }

        // Reject names that smuggle path separators before they ever reach storage.
        if (fileName.Contains('/', StringComparison.Ordinal)
            || fileName.Contains('\\', StringComparison.Ordinal)
            || fileName.Contains("..", StringComparison.Ordinal))
        {
            return "The file name is not valid.";
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        if (!BulkImportLimits.AllowedExtensions.Contains(extension))
        {
            return $"Only {string.Join(", ", BulkImportLimits.AllowedExtensions)} files are accepted.";
        }

        if (!BulkImportLimits.AllowedContentTypes.Contains(request.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            return "The file must be uploaded as CSV text.";
        }

        if (request.Length > BulkImportLimits.MaxFileBytes)
        {
            return $"The file exceeds the {Megabytes(BulkImportLimits.MaxFileBytes)} import limit.";
        }

        return null;
    }

    private static string Megabytes(long bytes) => $"{bytes / (1024 * 1024)} MB";

    /// <summary>
    /// Display label for a stored path or an upload name. Only the last segment is ever used,
    /// so neither can be turned into a probe of the server's directory layout.
    /// </summary>
    private static string DisplayName(string value)
    {
        var name = Path.GetFileName(value.Trim());

        if (string.IsNullOrWhiteSpace(name))
        {
            return "upload";
        }

        name = new string(name.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)).ToArray()).Trim();

        if (name.Length == 0)
        {
            return "upload";
        }

        return name.Length <= 120 ? name : name[^120..];
    }

    private static string Checksum(string checksum) =>
        checksum.Length <= 12 ? checksum : checksum[..12];

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static async Task<Stream> CopyToMemoryAsync(Stream content, CancellationToken cancellationToken)
    {
        var memory = new MemoryStream();
        await content.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;
        return memory;
    }

    private static string FullName(IReadOnlyDictionary<Guid, string> names, Guid id) =>
        names.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name) ? name : "Unknown account";

    /// <summary>Takes reference labels up to the display ceiling.</summary>
    private static IReadOnlyList<string> DescribeValues(IEnumerable<string> labels, int ceiling) =>
        labels.Take(ceiling).ToList();

    private static string ValueLabel(string code, string name) =>
        string.IsNullOrWhiteSpace(code) ? name : $"{code} \u2014 {name}";

    private static string StatusWords(ImportStatus status) => status switch
    {
        ImportStatus.Pending => "waiting for validation",
        ImportStatus.Processing => "already importing",
        ImportStatus.Completed => "already imported",
        ImportStatus.CompletedWithErrors => "already imported with rejected rows",
        ImportStatus.Failed => "failed",
        ImportStatus.Cancelled => "cancelled",
        ImportStatus.Validated => "waiting for confirmation",
        _ => "not importable"
    };

    private static string? DescribeAlreadyRun(ImportStatus status) => status switch
    {
        ImportStatus.Completed or ImportStatus.CompletedWithErrors => "This import has already been run.",
        ImportStatus.Cancelled => "This import was cancelled.",
        ImportStatus.Failed => "This import failed and cannot be resumed.",
        _ => null
    };

    private static ImportJobConfiguration? ReadConfiguration(string? configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ImportJobConfiguration>(configuration, JsonOptions);
        }
        catch (JsonException)
        {
            // Unreadable metadata degrades to "no configuration" rather than throwing.
            return null;
        }
    }

    private BulkImportJobDto ToDto(
        ImportJob job,
        string createdByName,
        RowTotals? totals,
        bool canImport,
        bool awaitingValidation,
        string? blockedBecause = null)
    {
        var configuration = ReadConfiguration(job.Configuration);

        return new BulkImportJobDto
        {
            Id = job.Id,
            Name = job.Name,
            Description = job.Description,
            ImportType = job.ImportType,
            Status = job.Status,
            // The stored name is a generated token, so the label captured at upload is shown.
            FileName = string.IsNullOrWhiteSpace(configuration?.FileName)
                ? DisplayName(job.SourceFilePath)
                : configuration!.FileName!,
            FileSizeBytes = job.SourceFileSizeBytes,
            ContentType = job.SourceFileContentType,
            TotalRows = totals?.StagedRows ?? job.TotalRecords,
            ValidRows = totals?.Valid ?? Math.Max(0, job.TotalRecords - job.FailedRecords),
            InvalidRows = totals?.Invalid ?? 0,
            DuplicateRows = totals?.Duplicate ?? 0,
            WarningRows = totals?.Warning ?? 0,
            ImportedRows = totals?.Imported ?? job.SuccessfulRecords,
            ErrorCount = totals?.IssueCount ?? 0,
            CreatedByName = createdByName,
            CreatedAt = job.CreatedAt,
            StartedAt = job.StartedAt,
            CompletedAt = job.CompletedAt,
            CanImport = canImport,
            ImportBlockedBecause = blockedBecause ?? DescribeAlreadyRun(job.Status),
            AwaitingValidation = awaitingValidation
        };
    }

    private static OperationResult<T> Denial<T>(JobAccess access) => access switch
    {
        JobAccess.Missing => OperationResult<T>.NotFound("That import job no longer exists."),
        JobAccess.NotOwner => OperationResult<T>.Forbidden(
            "Only the operator who uploaded a file can continue its import."),
        _ => throw new InvalidOperationException("An available job has no denial.")
    };

    private static OperationResult<T> NeedsValidation<T>() => OperationResult<T>.ValidationFailed(
        new Dictionary<string, string[]> { [""] = ["Validate the file before previewing it."] });

    // ------------------------------------------------------------------------ persistence

    private async Task<StagedJob> LoadStagedJobAsync(
        Guid jobId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var job = await _context.ImportJobs
            .FirstOrDefaultAsync(candidate => candidate.Id == jobId, cancellationToken);

        if (job is null)
        {
            return new StagedJob
            {
                Job = new ImportJob { Id = jobId },
                CreatedByName = string.Empty,
                Access = JobAccess.Missing
            };
        }

        var creatorName = await _context.Set<ApplicationUser>().AsNoTracking()
            .Where(user => user.Id == job.CreatedById)
            .Select(user => (user.FirstName + " " + user.LastName).Trim())
            .FirstOrDefaultAsync(cancellationToken);

        if (job.CreatedById != actorUserId)
        {
            // The attempt itself is audited: a refused access is a security event.
            _audit.Record(
                AuditEntityTypeNames.ImportJob,
                job.Id,
                AuditActionNames.ImportAccessDenied,
                actorUserId,
                newValue: $"{job.Name} belongs to another operator");

            await _context.SaveChangesAsync(cancellationToken);

            return new StagedJob
            {
                Job = job,
                CreatedByName = string.Empty,
                Access = JobAccess.NotOwner
            };
        }

        var totals = await LoadTotalsAsync(job.Id, cancellationToken);

        return new StagedJob
        {
            Job = job,
            CreatedByName = string.IsNullOrWhiteSpace(creatorName) ? "Unknown account" : creatorName,
            Access = JobAccess.Available,
            Totals = totals
        };
    }

    private async Task<RowTotals> LoadTotalsAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var byStatus = await _context.ImportRecords.AsNoTracking()
            .Where(record => record.ImportJobId == jobId)
            .GroupBy(record => record.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var warningRows = await _context.ImportErrors.AsNoTracking()
            .Where(error => error.ImportJobId == jobId && error.Severity == ImportIssueSeverity.Warning)
            .Select(error => error.RecordNumber)
            .Distinct()
            .CountAsync(cancellationToken);

        var issueCount = await _context.ImportErrors.AsNoTracking()
            .CountAsync(error => error.ImportJobId == jobId, cancellationToken);

        int Count(ImportRecordStatus status) =>
            byStatus.FirstOrDefault(item => item.Status == status)?.Count ?? 0;

        return new RowTotals(
            StagedRows: byStatus.Sum(item => item.Count),
            Valid: Count(ImportRecordStatus.Valid),
            Invalid: Count(ImportRecordStatus.Invalid),
            Duplicate: Count(ImportRecordStatus.Duplicate),
            Imported: Count(ImportRecordStatus.Imported),
            Warning: warningRows,
            IssueCount: issueCount);
    }

    private async Task<BulkImportValidationDto> BuildPersistedValidationAsync(
        ImportJob job,
        StagedJob staged,
        CancellationToken cancellationToken)
    {
        var records = await _context.ImportRecords.AsNoTracking()
            .Where(record => record.ImportJobId == job.Id)
            .OrderBy(record => record.RecordNumber)
            .ToListAsync(cancellationToken);

        var issues = await LoadIssuesAsync(job.Id, cancellationToken);
        var totals = staged.Totals;
        var configuration = ReadConfiguration(job.Configuration);

        return new BulkImportValidationDto
        {
            Job = ToDto(job, staged.CreatedByName, totals,
                canImport: totals.Valid > 0 && job.Status == ImportStatus.Validated,
                awaitingValidation: false),
            TotalRows = records.Count,
            ValidRows = totals.Valid + totals.Imported,
            InvalidRows = totals.Invalid,
            DuplicateRows = totals.Duplicate,
            WarningRows = totals.Warning,
            Issues = issues,
            IssuesTruncated = totals.IssueCount > issues.Count,
            ProblemRows = records
                .Where(record => record.Status is ImportRecordStatus.Invalid or ImportRecordStatus.Duplicate)
                .OrderBy(record => record.RecordNumber)
                .Take(BulkImportLimits.MaxShownIssues)
                .Select(record => new BulkImportRowDto
                {
                    RowNumber = record.RecordNumber,
                    Status = record.Status,
                    RawText = record.RawData,
                    FirstIssue = record.ErrorMessage,
                    Issues = string.IsNullOrEmpty(record.ErrorMessage)
                        ? []
                        : [record.ErrorMessage]
                })
                .ToList(),
            ValidatedAt = records.Count == 0 ? job.CreatedAt : records.Max(record => record.ProcessedAt),
            SchemaName = configuration?.Schema ?? BulkImportSchema.CaseImportType
        };
    }

    private async Task<IReadOnlyList<BulkImportIssueDto>> LoadIssuesAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var rows = await _context.ImportErrors.AsNoTracking()
            .Where(error => error.ImportJobId == jobId)
            .OrderBy(error => error.RecordNumber)
            .ThenBy(error => error.ErrorCode)
            .Take(BulkImportLimits.MaxShownIssues)
            .Select(error => new { error.RecordNumber, error.ErrorCode, error.ErrorMessage, error.Severity })
            .ToListAsync(cancellationToken);

        return rows
            .Select(error => new BulkImportIssueDto
            {
                RowNumber = error.RecordNumber,
                Code = error.ErrorCode,
                Field = BulkImportIssueCodes.SplitField(error.ErrorCode),
                Message = error.ErrorMessage,
                Severity = error.Severity
            })
            .ToList();
    }

    private static BulkImportRowDto ToRowDto(ImportRecord record, string[] headerNames)
    {
        var cells = new List<string>(BulkImportSchema.AllNames.Count);
        var unavailable = false;

        if (record.RawData.Length > 0 && headerNames.Length > 0)
        {
            // The stored row text is re-parsed so the preview shows the columns in the
            // documented order, whatever order the file itself used.
            var reparsed = CsvReader.ReadSingleRecord(record.RawData, BulkImportLimits.MaxRawRowChars);

            if (reparsed is not null && reparsed.Fields.Count == headerNames.Length)
            {
                foreach (var column in BulkImportSchema.Columns)
                {
                    var index = Array.FindIndex(
                        headerNames,
                        name => string.Equals(name, column.Name, StringComparison.OrdinalIgnoreCase));

                    cells.Add(index >= 0 && index < reparsed.Fields.Count ? reparsed.Fields[index] : string.Empty);
                }
            }
            else
            {
                unavailable = true;
            }
        }
        else
        {
            unavailable = true;
        }

        return new BulkImportRowDto
        {
            RowNumber = record.RecordNumber,
            Status = record.Status,
            Cells = cells,
            RawText = record.RawData,
            CellsUnavailable = unavailable,
            FirstIssue = record.ErrorMessage,
            Issues = string.IsNullOrEmpty(record.ErrorMessage) ? [] : [record.ErrorMessage]
        };
    }

    private async Task ClearStagingAsync(Guid jobId, CancellationToken cancellationToken)
    {
        // Validation is re-runnable up to the point of confirmation, so stale staging is
        // replaced rather than appended to.
        var records = await _context.ImportRecords
            .Where(record => record.ImportJobId == jobId)
            .ToListAsync(cancellationToken);

        _context.ImportRecords.RemoveRange(records);

        var errors = await _context.ImportErrors
            .Where(error => error.ImportJobId == jobId)
            .ToListAsync(cancellationToken);

        _context.ImportErrors.RemoveRange(errors);

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Moves a job to Failed and records why. After a rollback the change tracker is cleared
    /// first, so none of the discarded case rows can ride along on this save.
    /// </summary>
    private async Task MarkFailedAsync(
        ImportJob job,
        string reason,
        Guid actorUserId,
        CancellationToken cancellationToken,
        string? detail = null)
    {
        var jobId = job.Id;
        var safeReason = Truncate(reason, 1_000);

        _context.ChangeTracker.Clear();

        var tracked = await _context.ImportJobs
            .FirstOrDefaultAsync(candidate => candidate.Id == jobId, cancellationToken);

        if (tracked is null)
        {
            return;
        }

        tracked.Status = ImportStatus.Failed;
        tracked.CompletedAt = _time.GetUtcNow().UtcDateTime;

        _audit.Record(
            AuditEntityTypeNames.ImportJob,
            tracked.Id,
            AuditActionNames.ImportFailed,
            actorUserId,
            newValue: detail is null ? safeReason : $"{Truncate(safeReason, 900)} ({detail})");

        await _context.SaveChangesAsync(cancellationToken);
    }
}
