using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NETRA.Domain.ImageInvestigation;
using NETRA.Domain.Cases;
using NETRA.Domain.Security;

namespace NETRA.Infrastructure.Data.Configurations;

public class ImageRecordConfiguration : IEntityTypeConfiguration<ImageRecord>
{
    public void Configure(EntityTypeBuilder<ImageRecord> builder)
    {
        builder.ToTable("ImageRecords");

        builder.Property(ir => ir.FileName).HasMaxLength(500).IsRequired();
        builder.Property(ir => ir.FileExtension).HasMaxLength(100).IsRequired();
        builder.Property(ir => ir.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(ir => ir.FileSizeBytes).IsRequired();
        builder.Property(ir => ir.StoragePath).HasMaxLength(1000).IsRequired();
        builder.Property(ir => ir.ChecksumSha256).HasMaxLength(64).IsRequired();
        builder.Property(ir => ir.ImageType).IsRequired();
        builder.Property(ir => ir.Metadata).HasMaxLength(2000);
        builder.Property(ir => ir.CameraInfo).HasMaxLength(500);
        builder.Property(ir => ir.UploadedAt).IsRequired();

        builder.HasIndex(ir => ir.CrimeCaseId);
        builder.HasIndex(ir => ir.ChecksumSha256).IsUnique();
        builder.HasIndex(ir => ir.UploadedById);
        builder.HasIndex(ir => ir.UploadedAt);
        builder.HasIndex(ir => new { ir.Latitude, ir.Longitude });

        builder.HasOne(ir => ir.CrimeCase)
            .WithMany(c => c.ImageRecords)
            .HasForeignKey(ir => ir.CrimeCaseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ir => ir.UploadedBy)
            .WithMany()
            .HasForeignKey(ir => ir.UploadedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(ir => ir.Matches)
            .WithOne(m => m.SourceImage)
            .HasForeignKey(m => m.SourceImageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ImageMatchConfiguration : IEntityTypeConfiguration<ImageMatch>
{
    public void Configure(EntityTypeBuilder<ImageMatch> builder)
    {
        builder.ToTable("ImageMatches");

        builder.Property(im => im.SimilarityScore).HasPrecision(5, 4).IsRequired();
        builder.Property(im => im.MatchType).IsRequired();
        builder.Property(im => im.MatchDetails).HasMaxLength(2000);
        builder.Property(im => im.DetectedAt).IsRequired();
        builder.Property(im => im.IsVerified).IsRequired();
        builder.Property(im => im.IsFalsePositive).IsRequired();
        builder.Property(im => im.VerifiedAt);

        builder.HasIndex(im => im.SourceImageId);
        builder.HasIndex(im => im.MatchedImageId);
        builder.HasIndex(im => im.SimilarityScore);
        builder.HasIndex(im => im.IsVerified);
        builder.HasIndex(im => im.VerifiedById);
        builder.HasIndex(im => new { im.SourceImageId, im.MatchedImageId }).IsUnique();

        builder.HasOne(im => im.SourceImage)
            .WithMany(ir => ir.Matches)
            .HasForeignKey(im => im.SourceImageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(im => im.MatchedImage)
            .WithMany()
            .HasForeignKey(im => im.MatchedImageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(im => im.VerifiedBy)
            .WithMany()
            .HasForeignKey(im => im.VerifiedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}