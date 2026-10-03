using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using NETRA.Application.Abstractions;
using NETRA.Application.ImageInvestigation;
using NETRA.Domain.Cases;
using NETRA.Domain.Enums;
using NETRA.Domain.ImageInvestigation;
using NETRA.Infrastructure.Data;

namespace NETRA.Infrastructure.Services;

/// <summary>
/// Orchestrates the bounded image investigation flow: server-side validation,
/// secure storage through <see cref="IFileStorageService"/>, ImageRecord
/// persistence, deterministic candidate search through <see cref="IImageMatchProvider"/>,
/// ImageMatch persistence, audit logging and DTO assembly.
///
/// The service performs no image or facial analysis of its own. Candidates are
/// investigative leads over records NETRA already stores — never identity
/// confirmations — and all processing is capped (upload size, candidate count).
/// </summary>
public sealed class ImageInvestigationService : IImageInvestigationService
{
    private readonly NetraDbContext _context;
    private readonly IFileStorageService _storage;
    private readonly IImageMatchProvider _provider;
    private readonly IAuditService _audit;

    public ImageInvestigationService(
        NetraDbContext context,
        IFileStorageService storage,
        IImageMatchProvider provider,
        IAuditService audit)
    {
        _context = context;
        _storage = storage;
        _provider = provider;
        _audit = audit;
    }

    public async Task<IReadOnlyList<ImageCaseOptionDto>> GetSelectableCasesAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.CrimeCases.AsNoTracking()
            .Where(crimeCase => crimeCase.Status != CaseStatus.Closed && crimeCase.Status != CaseStatus.Archived)
            .OrderByDescending(crimeCase => crimeCase.IncidentDate)
            .ThenBy(crimeCase => crimeCase.CaseNumber)
            .Take(ImageInvestigationLimits.MaxCaseOptions)
            .Select(crimeCase => new ImageCaseOptionDto
            {
                Id = crimeCase.Id,
                CaseNumber = crimeCase.CaseNumber,
                Title = crimeCase.Title
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<OperationResult<ImageInvestigationResultDto>> AnalyzeAsync(
        AnalyzeImageRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validationError = ValidateUpload(request);
        if (validationError is not null)
        {
            return OperationResult<ImageInvestigationResultDto>.ValidationFailed(
                new Dictionary<string, string[]> { ["File"] = [validationError] });
        }

        var crimeCase = await _context.CrimeCases.AsNoTracking()
            .FirstOrDefaultAsync(
                c => c.Id == request.CrimeCaseId
                     && c.Status != CaseStatus.Closed && c.Status != CaseStatus.Archived,
                cancellationToken);

        if (crimeCase is null)
        {
            return OperationResult<ImageInvestigationResultDto>.ValidationFailed(
                new Dictionary<string, string[]> { ["CrimeCaseId"] = ["Select the case this investigation image belongs to."] });
        }

        var stream = request.Content.CanSeek ? request.Content : await CopyToMemoryAsync(request.Content, cancellationToken);

        var formatError = await DetectSupportedFormatAsync(stream, cancellationToken);
        if (formatError is not null)
        {
            return OperationResult<ImageInvestigationResultDto>.ValidationFailed(
                new Dictionary<string, string[]> { ["File"] = [formatError] });
        }

        stream.Position = 0;
        var checksum = await ComputeChecksumAsync(stream, cancellationToken);
        stream.Position = 0;

        // The ImageRecords checksum column is unique: refuse silent duplicates instead
        // of storing the same bytes twice.
        if (await _context.ImageRecords.AsNoTracking()
                .AnyAsync(record => record.ChecksumSha256 == checksum, cancellationToken))
        {
            return OperationResult<ImageInvestigationResultDto>.ValidationFailed(
                new Dictionary<string, string[]> { ["File"] = ["An identical image is already stored in NETRA."] });
        }

        StoredFileResult stored;
        try
        {
            stream.Position = 0;
            stored = await _storage.SaveAsync(
                stream,
                request.OriginalFileName,
                request.ContentType,
                crimeCase.CaseNumber,
                cancellationToken);
        }
        catch (FileStorageException ex)
        {
            return OperationResult<ImageInvestigationResultDto>.ValidationFailed(
                new Dictionary<string, string[]> { ["File"] = [ex.Message] });
        }

        var record = new ImageRecord
        {
            CrimeCaseId = crimeCase.Id,
            // The client name is kept only as a display label; storage used a
            // server-generated name.
            FileName = Path.GetFileName(request.OriginalFileName.Trim()),
            FileExtension = Path.GetExtension(request.OriginalFileName).ToLowerInvariant(),
            ContentType = request.ContentType,
            FileSizeBytes = stored.SizeBytes,
            StoragePath = stored.StoragePath,
            ChecksumSha256 = stored.ChecksumSha256,
            ImageType = 1,
            UploadedAt = DateTime.UtcNow,
            UploadedById = actorUserId
        };

        _context.ImageRecords.Add(record);

        _audit.Record(
            AuditEntityTypeNames.ImageRecord,
            record.Id,
            AuditActionNames.EvidenceUploaded,
            actorUserId,
            newValue: $"{record.FileName} ({stored.SizeBytes} bytes)");

        _audit.Record(
            AuditEntityTypeNames.ImageRecord,
            record.Id,
            AuditActionNames.ImageAnalysisRequested,
            actorUserId,
            newValue: _provider.Name);

        var providerResult = await FindCandidatesSafeAsync(record, cancellationToken);

        foreach (var candidate in providerResult.Candidates)
        {
            _context.ImageMatches.Add(new ImageMatch
            {
                SourceImageId = record.Id,
                MatchedImageId = candidate.MatchedImageRecordId,
                SimilarityScore = candidate.SimilarityScore,
                MatchType = 1,
                MatchDetails = $"{_provider.Name} - investigative lead; verification required",
                DetectedAt = DateTime.UtcNow,
                IsVerified = false
            });
        }

        await _context.SaveChangesAsync(cancellationToken);

        var matches = await _context.ImageMatches.AsNoTracking()
            .Where(match => match.SourceImageId == record.Id)
            .OrderByDescending(match => match.SimilarityScore)
            .ThenBy(match => match.Id)
            .ToListAsync(cancellationToken);

        var result = await BuildResultAsync(record, crimeCase, matches, providerResult.ProviderAvailable, cancellationToken);
        return OperationResult<ImageInvestigationResultDto>.Success(result);
    }

    public async Task<ImageInvestigationResultDto?> GetResultAsync(
        Guid imageRecordId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var record = await _context.ImageRecords.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == imageRecordId, cancellationToken);

        if (record is null)
        {
            return null;
        }

        var crimeCase = await _context.CrimeCases.AsNoTracking()
            .FirstAsync(c => c.Id == record.CrimeCaseId, cancellationToken);

        var matches = await _context.ImageMatches.AsNoTracking()
            .Where(match => match.SourceImageId == record.Id)
            .OrderByDescending(match => match.SimilarityScore)
            .ThenBy(match => match.Id)
            .ToListAsync(cancellationToken);

        _audit.Record(
            AuditEntityTypeNames.ImageRecord,
            record.Id,
            AuditActionNames.ImageResultsViewed,
            actorUserId,
            newValue: $"{matches.Count} candidate(s)");

        // Viewing is a read-only flow, so flush the queued audit entry explicitly.
        await _context.SaveChangesAsync(cancellationToken);

        return await BuildResultAsync(record, crimeCase, matches, true, cancellationToken);
    }

    public async Task<ImageMatchDetailDto?> GetMatchDetailAsync(
        Guid matchId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var match = await _context.ImageMatches.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == matchId, cancellationToken);

        if (match is null)
        {
            return null;
        }

        var matchedRecord = await _context.ImageRecords.AsNoTracking()
            .FirstAsync(r => r.Id == match.MatchedImageId, cancellationToken);

        var (person, caseCount, locationCount) = await ResolveCandidateAsync(matchedRecord, cancellationToken);

        var associatedCases = await _context.CasePersons.AsNoTracking()
            .Where(association => person != null && association.PersonId == person.Id)
            .OrderBy(association => association.AssociatedAt)
            .ThenBy(association => association.Id)
            .Take(10)
            .Select(association => new
            {
                association.CrimeCase.Id,
                association.CrimeCase.CaseNumber,
                association.CrimeCase.Title,
                association.CrimeCase.Status,
                association.CrimeCase.IncidentDate
            })
            .ToListAsync(cancellationToken);

        var relatedLocations = await _context.CaseLocations.AsNoTracking()
            .Where(association => association.CrimeCaseId == matchedRecord.CrimeCaseId)
            .OrderBy(association => association.AssociatedAt)
            .ThenBy(association => association.Id)
            .Take(10)
            .Select(association => new ImageRelatedLocationDto
            {
                Name = association.Location.Name,
                Type = string.IsNullOrWhiteSpace(association.Location.Type) ? "Location" : association.Location.Type
            })
            .ToListAsync(cancellationToken);

        _audit.Record(
            AuditEntityTypeNames.ImageMatch,
            match.Id,
            AuditActionNames.ImageMatchViewed,
            actorUserId,
            newValue: $"score {match.SimilarityScore:P1} (synthetic)");

        // Viewing is a read-only flow, so flush the queued audit entry explicitly.
        await _context.SaveChangesAsync(cancellationToken);

        return new ImageMatchDetailDto
        {
            MatchId = match.Id,
            SourceImageRecordId = match.SourceImageId,
            Reference = CandidateReference(matchedRecord.Id),
            DisplayName = person is not null ? person.FullName : matchedRecord.FileName,
            SimilarityScore = match.SimilarityScore,
            Method = _provider.Name,
            DetectedAt = match.DetectedAt,
            AssociatedCases = person is null
                ? Array.Empty<ImageAssociatedCaseDto>()
                : associatedCases
                    .Select(crimeCase => new ImageAssociatedCaseDto
                    {
                        Id = crimeCase.Id,
                        CaseNumber = crimeCase.CaseNumber,
                        Title = crimeCase.Title,
                        Status = crimeCase.Status,
                        IncidentDateLabel = crimeCase.IncidentDate.ToString("dd MMM yyyy")
                    })
                    .ToList(),
            RelatedLocations = relatedLocations
        };
    }

    public async Task<StoredImageContent?> OpenImageAsync(
        Guid imageRecordId,
        CancellationToken cancellationToken = default)
    {
        var record = await _context.ImageRecords.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == imageRecordId, cancellationToken);

        if (record is null)
        {
            return null;
        }

        var content = await _storage.OpenReadAsync(record.StoragePath, cancellationToken);
        if (content is null)
        {
            return null;
        }

        return new StoredImageContent(record.ContentType, record.FileName, content);
    }

    private static string? ValidateUpload(AnalyzeImageRequest request)
    {
        var fileName = request.OriginalFileName?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(fileName) || request.Length <= 0)
        {
            return "Choose an image file to upload.";
        }

        // Reject names that try to smuggle path separators; the storage service never
        // uses the client name as a path, but the label must stay clean too.
        if (fileName.Contains('/') || fileName.Contains('\\') || fileName.Contains(".."))
        {
            return "The file name is not valid.";
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!ImageInvestigationLimits.AllowedExtensions.Contains(extension))
        {
            return $"Only {string.Join(", ", ImageInvestigationLimits.AllowedExtensions)} images are accepted.";
        }

        if (!ImageInvestigationLimits.AllowedContentTypes.Contains(request.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            return "The file content type must be image/jpeg, image/png or image/webp.";
        }

        if (request.Length > ImageInvestigationLimits.MaxUploadBytes)
        {
            return $"The file exceeds the maximum size of {ImageInvestigationLimits.MaxUploadBytes / (1024 * 1024)} MB.";
        }

        return null;
    }

    /// <summary>
    /// Confirms the leading bytes really encode an accepted image format, so a
    /// renamed or malformed file cannot enter the store.
    /// </summary>
    private static async Task<string?> DetectSupportedFormatAsync(Stream stream, CancellationToken cancellationToken)
    {
        stream.Position = 0;
        var header = new byte[12];
        var read = 0;
        while (read < header.Length)
        {
            var got = await stream.ReadAsync(header.AsMemory(read, header.Length - read), cancellationToken);
            if (got == 0)
            {
                break;
            }

            read += got;
        }

        stream.Position = 0;

        if (read < 12)
        {
            return "The file is too small to be a valid image.";
        }

        var isJpeg = header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
        var isPng = header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
                    && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A;
        var isWebp = header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F'
                     && header[8] == (byte)'W' && header[9] == (byte)'E' && header[10] == (byte)'B' && header[11] == (byte)'P';

        return isJpeg || isPng || isWebp
            ? null
            : "The file content does not match a supported image format.";
    }

    private static async Task<Stream> CopyToMemoryAsync(Stream content, CancellationToken cancellationToken)
    {
        var memory = new MemoryStream();
        await content.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;
        return memory;
    }

    private static async Task<string> ComputeChecksumAsync(Stream stream, CancellationToken cancellationToken)
    {
        stream.Position = 0;
        using var hasher = SHA256.Create();
        var hash = await hasher.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<ImageMatchResult> FindCandidatesSafeAsync(
        ImageRecord record,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _provider.FindCandidatesAsync(new ImageMatchQuery
            {
                SourceImageRecordId = record.Id,
                SourceChecksumSha256 = record.ChecksumSha256
            }, cancellationToken);
        }
        catch (Exception)
        {
            // A provider failure must not surface as a 500 or leak internals; the
            // analysis stands with zero candidates and an honest unavailable note.
            return ImageMatchResult.Unavailable;
        }
    }

    private async Task<ImageInvestigationResultDto> BuildResultAsync(
        ImageRecord record,
        CrimeCase crimeCase,
        IReadOnlyList<ImageMatch> matches,
        bool providerAvailable,
        CancellationToken cancellationToken)
    {
        var candidates = new List<ImageCandidateDto>(matches.Count);

        foreach (var match in matches)
        {
            var matched = await _context.ImageRecords.AsNoTracking()
                .Where(r => r.Id == match.MatchedImageId)
                .Select(r => new
                {
                    Record = r,
                    CaseNumber = r.CrimeCase.CaseNumber
                })
                .FirstAsync(cancellationToken);

            var matchedRecord = matched.Record;
            var (person, caseCount, locationCount) = await ResolveCandidateAsync(matchedRecord, cancellationToken);

            candidates.Add(new ImageCandidateDto
            {
                MatchId = match.Id,
                MatchedImageRecordId = matchedRecord.Id,
                Reference = CandidateReference(matchedRecord.Id),
                DisplayName = person is not null ? person.FullName : matchedRecord.FileName,
                DisplaySubtitle = person is not null
                    ? $"Person record - case {matched.CaseNumber}"
                    : $"Stored image - case {matched.CaseNumber}",
                SimilarityScore = match.SimilarityScore,
                AssociatedCaseCount = person is null ? 1 : caseCount,
                RelatedLocationCount = locationCount,
                SourceCaseNumber = crimeCase.CaseNumber,
                PersonId = person?.Id
            });
        }

        return new ImageInvestigationResultDto
        {
            ImageRecordId = record.Id,
            FileName = record.FileName,
            ContentType = record.ContentType,
            FileSizeBytes = record.FileSizeBytes,
            ChecksumSha256 = record.ChecksumSha256,
            UploadedAt = record.UploadedAt,
            CrimeCaseId = crimeCase.Id,
            CaseNumber = crimeCase.CaseNumber,
            CaseTitle = crimeCase.Title,
            ProviderName = _provider.Name,
            ProviderAvailable = providerAvailable,
            Candidates = candidates
        };
    }

    /// <summary>
    /// Resolves the person represented by a stored image: the suspect (or first
    /// associated person) of the image's case, plus deterministic case/location counts.
    /// </summary>
    private async Task<(NETRA.Domain.People.Person? Person, int CaseCount, int LocationCount)> ResolveCandidateAsync(
        ImageRecord matchedRecord,
        CancellationToken cancellationToken)
    {
        var personIds = await _context.CasePersons.AsNoTracking()
            .Where(association => association.CrimeCaseId == matchedRecord.CrimeCaseId)
            .OrderBy(association => association.Role == PersonRole.Suspect ? 0
                : association.Role == PersonRole.Victim ? 1
                : association.Role == PersonRole.Witness ? 2 : 3)
            .ThenBy(association => association.AssociatedAt)
            .ThenBy(association => association.Id)
            .Select(association => new { association.PersonId, association.Person.IsActive })
            .ToListAsync(cancellationToken);

        var preferred = personIds.FirstOrDefault(x => x.IsActive)?.PersonId;

        if (preferred is null)
        {
            return (null, 1, await CountCaseLocationsAsync(matchedRecord.CrimeCaseId, cancellationToken));
        }

        var person = await _context.Persons.AsNoTracking()
            .FirstAsync(p => p.Id == preferred, cancellationToken);

        var caseCount = await _context.CasePersons.AsNoTracking()
            .CountAsync(association => association.PersonId == person.Id, cancellationToken);

        var locationCount = await _context.CaseLocations.AsNoTracking()
            .Where(association => association.CrimeCase.CasePersons.Any(cp => cp.PersonId == person.Id))
            .Select(association => association.LocationId)
            .Distinct()
            .CountAsync(cancellationToken);

        return (person, caseCount, locationCount);
    }

    private async Task<int> CountCaseLocationsAsync(Guid crimeCaseId, CancellationToken cancellationToken)
    {
        return await _context.CaseLocations.AsNoTracking()
            .CountAsync(association => association.CrimeCaseId == crimeCaseId, cancellationToken);
    }

    private static string CandidateReference(Guid matchedImageRecordId)
    {
        // Stable synthetic display reference derived from the stored record id.
        var bytes = matchedImageRecordId.ToByteArray();
        var value = (int)((uint)BitConverter.ToInt32(bytes, 0) % 90u) + 10;
        return $"DEMO-CAND-{value:D2}";
    }
}
