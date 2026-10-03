using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NETRA.Domain.Reports;
using NETRA.Domain.Enums;
using NETRA.Domain.Cases;
using NETRA.Domain.Security;

namespace NETRA.Infrastructure.Data.Configurations;

public class ReportConfiguration : IEntityTypeConfiguration<Report>
{
    public void Configure(EntityTypeBuilder<Report> builder)
    {
        builder.ToTable("Reports");

        builder.Property(r => r.Title).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(4000);
        builder.Property(r => r.ReportType).HasConversion<int>().IsRequired();
        builder.Property(r => r.Parameters).HasMaxLength(4000);
        builder.Property(r => r.GeneratedFilePath).HasMaxLength(1000);
        builder.Property(r => r.GeneratedFileContentType).HasMaxLength(100);
        builder.Property(r => r.Status).HasConversion<int>().IsRequired();
        builder.Property(r => r.Source).HasConversion<int>().IsRequired();
        builder.Property(r => r.Content).HasMaxLength(8000);
        builder.Property(r => r.RequestedAt).IsRequired();
        builder.Property(r => r.CreatedAt).IsRequired();

        builder.HasIndex(r => r.CrimeCaseId);
        builder.HasIndex(r => r.ReportType);
        builder.HasIndex(r => r.Status);
        builder.HasIndex(r => r.Source);
        builder.HasIndex(r => r.RequestedById);
        builder.HasIndex(r => r.GeneratedById);
        builder.HasIndex(r => r.RequestedAt);

        builder.HasOne(r => r.CrimeCase)
            .WithMany(c => c.Reports)
            .HasForeignKey(r => r.CrimeCaseId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(r => r.RequestedBy)
            .WithMany()
            .HasForeignKey(r => r.RequestedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.GeneratedBy)
            .WithMany()
            .HasForeignKey(r => r.GeneratedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}