using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NETRA.Domain.Enums;
using NETRA.Domain.Security;
using NETRA.Domain.Cases;

namespace NETRA.Domain.ImageInvestigation;

public class ImageRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CrimeCaseId { get; set; }

    [ForeignKey(nameof(CrimeCaseId))]
    public virtual CrimeCase CrimeCase { get; set; } = null!;

    [MaxLength(500)]
    public string FileName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string FileExtension { get; set; } = string.Empty;

    [MaxLength(100)]
    public string ContentType { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    [MaxLength(1000)]
    public string StoragePath { get; set; } = string.Empty;

    [MaxLength(64)]
    public string ChecksumSha256 { get; set; } = string.Empty;

    public int ImageType { get; set; } = 1;

    [MaxLength(2000)]
    public string? Metadata { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public DateTime? CapturedAt { get; set; }

    [MaxLength(500)]
    public string? CameraInfo { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public Guid UploadedById { get; set; }

    [ForeignKey(nameof(UploadedById))]
    public virtual ApplicationUser UploadedBy { get; set; } = null!;

    public virtual ICollection<ImageMatch> Matches { get; set; } = new List<ImageMatch>();
}

public class ImageMatch
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SourceImageId { get; set; }

    [ForeignKey(nameof(SourceImageId))]
    public virtual ImageRecord SourceImage { get; set; } = null!;

    public Guid MatchedImageId { get; set; }

    [ForeignKey(nameof(MatchedImageId))]
    public virtual ImageRecord MatchedImage { get; set; } = null!;

    public decimal SimilarityScore { get; set; }

    public int MatchType { get; set; } = 1;

    [MaxLength(2000)]
    public string? MatchDetails { get; set; }

    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

    public Guid? VerifiedById { get; set; }

    [ForeignKey(nameof(VerifiedById))]
    public virtual ApplicationUser? VerifiedBy { get; set; }

    public bool IsVerified { get; set; } = false;

    public DateTime? VerifiedAt { get; set; }

    public bool IsFalsePositive { get; set; } = false;
}