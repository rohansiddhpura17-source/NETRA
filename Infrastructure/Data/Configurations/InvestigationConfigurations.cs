using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NETRA.Domain.Investigation;
using NETRA.Domain.Security;
using NETRA.Domain.People;

namespace NETRA.Infrastructure.Data.Configurations;

public class InvestigationConfiguration : IEntityTypeConfiguration<Investigation>
{
    public void Configure(EntityTypeBuilder<Investigation> builder)
    {
        builder.ToTable("Investigations");

        builder.Property(i => i.Title).HasMaxLength(200).IsRequired();
        builder.Property(i => i.Summary).HasMaxLength(4000);
        builder.Property(i => i.StartedAt).IsRequired();
        builder.Property(i => i.CompletedAt);
        builder.Property(i => i.Status).IsRequired();
        builder.Property(i => i.CreatedAt).IsRequired();
        builder.Property(i => i.UpdatedAt).IsRequired();

        builder.HasIndex(i => i.CrimeCaseId).IsUnique();
        builder.HasIndex(i => i.LeadInvestigatorId);
        builder.HasIndex(i => i.SupervisingOfficerId);
        builder.HasIndex(i => i.Status);

        builder.HasOne(i => i.CrimeCase)
            .WithOne(c => c.Investigation)
            .HasForeignKey<Investigation>(i => i.CrimeCaseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.LeadInvestigator)
            .WithMany()
            .HasForeignKey(i => i.LeadInvestigatorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.SupervisingOfficer)
            .WithMany()
            .HasForeignKey(i => i.SupervisingOfficerId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(i => i.Notes)
            .WithOne(n => n.Investigation)
            .HasForeignKey(n => n.InvestigationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(i => i.TimelineEvents)
            .WithOne(te => te.Investigation)
            .HasForeignKey(te => te.InvestigationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(i => i.EvidenceItems)
            .WithOne(e => e.Investigation)
            .HasForeignKey(e => e.InvestigationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class InvestigationNoteConfiguration : IEntityTypeConfiguration<InvestigationNote>
{
    public void Configure(EntityTypeBuilder<InvestigationNote> builder)
    {
        builder.ToTable("InvestigationNotes");

        builder.Property(n => n.Content).HasMaxLength(4000).IsRequired();
        builder.Property(n => n.NoteType).IsRequired();
        builder.Property(n => n.CreatedAt).IsRequired();
        builder.Property(n => n.IsPrivate).IsRequired();

        builder.HasIndex(n => n.InvestigationId);
        builder.HasIndex(n => n.CreatedById);
        builder.HasIndex(n => n.CreatedAt);

        builder.HasOne(n => n.Investigation)
            .WithMany(i => i.Notes)
            .HasForeignKey(n => n.InvestigationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(n => n.CreatedBy)
            .WithMany()
            .HasForeignKey(n => n.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class TimelineEventConfiguration : IEntityTypeConfiguration<TimelineEvent>
{
    public void Configure(EntityTypeBuilder<TimelineEvent> builder)
    {
        builder.ToTable("TimelineEvents");

        builder.Property(te => te.Title).HasMaxLength(200).IsRequired();
        builder.Property(te => te.Description).HasMaxLength(2000);
        builder.Property(te => te.EventDateTime).IsRequired();
        builder.Property(te => te.EventType).IsRequired();
        builder.Property(te => te.Location).HasMaxLength(100);
        builder.Property(te => te.RelatedEntityType).HasMaxLength(100);
        builder.Property(te => te.CreatedAt).IsRequired();

        builder.HasIndex(te => te.InvestigationId);
        builder.HasIndex(te => te.EventDateTime);
        builder.HasIndex(te => te.EventType);
        builder.HasIndex(te => te.RelatedEntityId);
        builder.HasIndex(te => te.CreatedById);

        builder.HasOne(te => te.Investigation)
            .WithMany(i => i.TimelineEvents)
            .HasForeignKey(te => te.InvestigationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(te => te.CreatedBy)
            .WithMany()
            .HasForeignKey(te => te.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class EvidenceConfiguration : IEntityTypeConfiguration<Evidence>
{
    public void Configure(EntityTypeBuilder<Evidence> builder)
    {
        builder.ToTable("Evidence");

        builder.Property(e => e.Title).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(2000);
        builder.Property(e => e.EvidenceType).IsRequired();
        builder.Property(e => e.EvidenceStatus).IsRequired();
        builder.Property(e => e.CollectedBy).HasMaxLength(100);
        builder.Property(e => e.CollectionLocation).HasMaxLength(500);
        builder.Property(e => e.ChainOfCustody).HasMaxLength(200);
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.UpdatedAt);

        builder.HasIndex(e => e.InvestigationId);
        builder.HasIndex(e => e.PersonId);
        builder.HasIndex(e => e.EvidenceType);
        builder.HasIndex(e => e.EvidenceStatus);
        builder.HasIndex(e => e.CollectedByUserId);
        builder.HasIndex(e => e.CreatedAt);

        builder.HasOne(e => e.Investigation)
            .WithMany(i => i.EvidenceItems)
            .HasForeignKey(e => e.InvestigationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Person)
            .WithMany(p => p.EvidenceItems)
            .HasForeignKey(e => e.PersonId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.CollectedByUser)
            .WithMany()
            .HasForeignKey(e => e.CollectedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(e => e.Files)
            .WithOne(f => f.Evidence)
            .HasForeignKey(f => f.EvidenceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class EvidenceFileConfiguration : IEntityTypeConfiguration<EvidenceFile>
{
    public void Configure(EntityTypeBuilder<EvidenceFile> builder)
    {
        builder.ToTable("EvidenceFiles");

        builder.Property(f => f.FileName).HasMaxLength(500).IsRequired();
        builder.Property(f => f.FileExtension).HasMaxLength(100).IsRequired();
        builder.Property(f => f.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(f => f.FileSizeBytes).IsRequired();
        builder.Property(f => f.StoragePath).HasMaxLength(1000).IsRequired();
        builder.Property(f => f.ChecksumSha256).HasMaxLength(64);
        builder.Property(f => f.UploadedAt).IsRequired();
        builder.Property(f => f.IsPrimary).IsRequired();
        builder.Property(f => f.Description).HasMaxLength(500);

        builder.HasIndex(f => f.EvidenceId);
        builder.HasIndex(f => f.ChecksumSha256);
        builder.HasIndex(f => f.UploadedById);

        builder.HasOne(f => f.Evidence)
            .WithMany(e => e.Files)
            .HasForeignKey(f => f.EvidenceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(f => f.UploadedBy)
            .WithMany()
            .HasForeignKey(f => f.UploadedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}