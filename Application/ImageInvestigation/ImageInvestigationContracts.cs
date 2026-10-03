using NETRA.Domain.Enums;

namespace NETRA.Application.ImageInvestigation;

/// <summary>
/// Bounded, deterministic image-assisted investigation over records NETRA already
/// stores. This is an academic/demo capability: the match provider is synthetic and
/// results are investigative leads only — they never establish identity or guilt.
/// </summary>
public static class ImageInvestigationLimits
{
    /// <summary>Uploads larger than this are rejected before anything is stored.</summary>
    public const long MaxUploadBytes = 10 * 1024 * 1024;

    /// <summary>Maximum candidate matches returned and persisted for one analysis.</summary>
    public const int MaxCandidates = 4;

    /// <summary>Case options rendered in the upload form.</summary>
    public const int MaxCaseOptions = 200;

    public static readonly IReadOnlyCollection<string> AllowedExtensions =
        [".jpg", ".jpeg", ".png", ".webp"];

    public static readonly IReadOnlyCollection<string> AllowedContentTypes =
        ["image/jpeg", "image/png", "image/webp"];
}

/// <summary>A validated upload plus the case the investigation belongs to.</summary>
public sealed class AnalyzeImageRequest
{
    public Guid CrimeCaseId { get; init; }

    /// <summary>Client-supplied name. Retained only as a display label, never as a path.</summary>
    public string OriginalFileName { get; init; } = string.Empty;

    public string ContentType { get; init; } = string.Empty;

    public long Length { get; init; }

    public Stream Content { get; init; } = Stream.Null;
}

public sealed class ImageCaseOptionDto
{
    public Guid Id { get; init; }

    public string CaseNumber { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;
}

/// <summary>One synthetic candidate produced by the match provider.</summary>
public sealed class ImageProviderCandidate
{
    /// <summary>The stored ImageRecord the upload was compared against.</summary>
    public Guid MatchedImageRecordId { get; init; }

    /// <summary>Deterministic similarity in [0, 1]. Synthetic — never an identity claim.</summary>
    public decimal SimilarityScore { get; init; }

    public string Method { get; init; } = "Deterministic demo comparison";
}

public sealed class ImageMatchQuery
{
    public Guid SourceImageRecordId { get; init; }

    public string SourceChecksumSha256 { get; init; } = string.Empty;

    public int MaxCandidates { get; init; } = ImageInvestigationLimits.MaxCandidates;
}

public sealed class ImageMatchResult
{
    public static readonly ImageMatchResult Unavailable = new()
    {
        ProviderAvailable = false,
        Candidates = Array.Empty<ImageProviderCandidate>()
    };

    public bool ProviderAvailable { get; init; } = true;

    public IReadOnlyList<ImageProviderCandidate> Candidates { get; init; } =
        Array.Empty<ImageProviderCandidate>();
}

/// <summary>
/// Abstraction over a candidate search. The only shipped implementation is a
/// deterministic mock over NETRA's own stored images; no external, biometric or
/// real-world identity systems may be wired to this interface.
/// </summary>
public interface IImageMatchProvider
{
    string Name { get; }

    Task<ImageMatchResult> FindCandidatesAsync(
        ImageMatchQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>A candidate as displayed on the results page.</summary>
public sealed class ImageCandidateDto
{
    public Guid MatchId { get; init; }

    public Guid MatchedImageRecordId { get; init; }

    /// <summary>Synthetic candidate reference such as "DEMO-CAND-01".</summary>
    public string Reference { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string DisplaySubtitle { get; init; } = string.Empty;

    public decimal SimilarityScore { get; init; }

    public int AssociatedCaseCount { get; init; }

    public int RelatedLocationCount { get; init; }

    public string? SourceCaseNumber { get; init; }

    public Guid? PersonId { get; init; }
}

public sealed class ImageInvestigationResultDto
{
    public Guid ImageRecordId { get; init; }

    public string FileName { get; init; } = string.Empty;

    public string ContentType { get; init; } = string.Empty;

    public long FileSizeBytes { get; init; }

    public string ChecksumSha256 { get; init; } = string.Empty;

    public DateTime UploadedAt { get; init; }

    public Guid CrimeCaseId { get; init; }

    public string CaseNumber { get; init; } = string.Empty;

    public string CaseTitle { get; init; } = string.Empty;

    public string ProviderName { get; init; } = string.Empty;

    public bool ProviderAvailable { get; init; }

    public IReadOnlyList<ImageCandidateDto> Candidates { get; init; } =
        Array.Empty<ImageCandidateDto>();
}

public sealed class ImageAssociatedCaseDto
{
    public Guid Id { get; init; }

    public string CaseNumber { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public CaseStatus Status { get; init; }

    public string IncidentDateLabel { get; init; } = string.Empty;
}

public sealed class ImageRelatedLocationDto
{
    public string Name { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;
}

public sealed class ImageMatchDetailDto
{
    public Guid MatchId { get; init; }

    public Guid SourceImageRecordId { get; init; }

    public string Reference { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public decimal SimilarityScore { get; init; }

    public string Method { get; init; } = string.Empty;

    public DateTime DetectedAt { get; init; }

    public IReadOnlyList<ImageAssociatedCaseDto> AssociatedCases { get; init; } =
        Array.Empty<ImageAssociatedCaseDto>();

    public IReadOnlyList<ImageRelatedLocationDto> RelatedLocations { get; init; } =
        Array.Empty<ImageRelatedLocationDto>();
}

/// <summary>Content stream for the authorised preview endpoint. Never a raw path.</summary>
public sealed class StoredImageContent
{
    public StoredImageContent(string contentType, string fileName, Stream content)
    {
        ContentType = contentType;
        FileName = fileName;
        Content = content;
    }

    public string ContentType { get; }

    public string FileName { get; }

    public Stream Content { get; }
}

public interface IImageInvestigationService
{
    /// <summary>Cases the current user can attach an investigation image to.</summary>
    Task<IReadOnlyList<ImageCaseOptionDto>> GetSelectableCasesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates, stores and records the upload, runs the match provider and
    /// persists the candidate matches. Audits the upload and the analysis.
    /// </summary>
    Task<NETRA.Application.Abstractions.OperationResult<ImageInvestigationResultDto>> AnalyzeAsync(
        AnalyzeImageRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Loads a previous analysis result set. Audits the viewing.</summary>
    Task<ImageInvestigationResultDto?> GetResultAsync(
        Guid imageRecordId,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Loads one persisted match with its related records. Audits the viewing.</summary>
    Task<ImageMatchDetailDto?> GetMatchDetailAsync(
        Guid matchId,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Opens the stored image bytes for an authorised inline preview.</summary>
    Task<StoredImageContent?> OpenImageAsync(
        Guid imageRecordId,
        CancellationToken cancellationToken = default);
}
