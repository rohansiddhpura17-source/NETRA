using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NETRA.Domain.Import;
using NETRA.Domain.Enums;
using NETRA.Domain.Security;

namespace NETRA.Infrastructure.Data.Configurations;

public class ImportJobConfiguration : IEntityTypeConfiguration<ImportJob>
{
    public void Configure(EntityTypeBuilder<ImportJob> builder)
    {
        builder.ToTable("ImportJobs");

        builder.Property(ij => ij.Name).HasMaxLength(200).IsRequired();
        builder.Property(ij => ij.Description).HasMaxLength(4000);
        builder.Property(ij => ij.ImportType).HasConversion<int>().IsRequired();
        builder.Property(ij => ij.Status).HasConversion<int>().IsRequired();
        builder.Property(ij => ij.SourceFilePath).HasMaxLength(1000).IsRequired();
        builder.Property(ij => ij.SourceFileSizeBytes).IsRequired();
        builder.Property(ij => ij.SourceFileContentType).HasMaxLength(100).IsRequired();
        builder.Property(ij => ij.Configuration).HasMaxLength(4000);
        builder.Property(ij => ij.StartedAt).IsRequired();
        builder.Property(ij => ij.CompletedAt);
        builder.Property(ij => ij.CreatedAt).IsRequired();

        builder.HasIndex(ij => ij.Status);
        builder.HasIndex(ij => ij.CreatedById);
        builder.HasIndex(ij => ij.StartedAt);

        builder.HasOne(ij => ij.CreatedBy)
            .WithMany()
            .HasForeignKey(ij => ij.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(ij => ij.Records)
            .WithOne(r => r.ImportJob)
            .HasForeignKey(r => r.ImportJobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(ij => ij.Errors)
            .WithOne(e => e.ImportJob)
            .HasForeignKey(e => e.ImportJobId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ImportRecordConfiguration : IEntityTypeConfiguration<ImportRecord>
{
    public void Configure(EntityTypeBuilder<ImportRecord> builder)
    {
        builder.ToTable("ImportRecords");

        builder.Property(ir => ir.RawData).HasMaxLength(4000).IsRequired();
        builder.Property(ir => ir.Status).HasConversion<int>().IsRequired();
        builder.Property(ir => ir.CreatedEntityType).HasMaxLength(100);
        builder.Property(ir => ir.ErrorMessage).HasMaxLength(2000);
        builder.Property(ir => ir.ProcessedAt).IsRequired();

        builder.HasIndex(ir => ir.ImportJobId);
        builder.HasIndex(ir => ir.RecordNumber);
        builder.HasIndex(ir => ir.Status);
        builder.HasIndex(ir => ir.CreatedEntityId);

        builder.HasOne(ir => ir.ImportJob)
            .WithMany(ij => ij.Records)
            .HasForeignKey(ir => ir.ImportJobId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ImportErrorConfiguration : IEntityTypeConfiguration<ImportError>
{
    public void Configure(EntityTypeBuilder<ImportError> builder)
    {
        builder.ToTable("ImportErrors");

        builder.Property(ie => ie.ErrorCode).HasMaxLength(100).IsRequired();
        builder.Property(ie => ie.ErrorMessage).HasMaxLength(2000).IsRequired();
        builder.Property(ie => ie.RawData).HasMaxLength(4000);
        builder.Property(ie => ie.Severity).HasConversion<int>().IsRequired();
        builder.Property(ie => ie.OccurredAt).IsRequired();
        builder.Property(ie => ie.IsResolved).IsRequired();
        builder.Property(ie => ie.ResolvedAt);

        builder.HasIndex(ie => ie.ImportJobId);
        builder.HasIndex(ie => ie.RecordNumber);
        builder.HasIndex(ie => ie.ErrorCode);
        builder.HasIndex(ie => ie.IsResolved);
        builder.HasIndex(ie => ie.ResolvedById);

        builder.HasOne(ie => ie.ImportJob)
            .WithMany(ij => ij.Errors)
            .HasForeignKey(ie => ie.ImportJobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ie => ie.ResolvedBy)
            .WithMany()
            .HasForeignKey(ie => ie.ResolvedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}