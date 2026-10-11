using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NETRA.Domain.Intelligence;
using NETRA.Domain.Enums;
using NETRA.Domain.Security;
using NETRA.Domain.Cases;
using NETRA.Domain.People;
using NETRA.Domain.Geography;

namespace NETRA.Infrastructure.Data.Configurations;

public class RiskAssessmentConfiguration : IEntityTypeConfiguration<RiskAssessment>
{
    public void Configure(EntityTypeBuilder<RiskAssessment> builder)
    {
        builder.ToTable("RiskAssessments");

        builder.Property(ra => ra.Title).HasMaxLength(200).IsRequired();
        builder.Property(ra => ra.Assessment).HasMaxLength(4000);
        builder.Property(ra => ra.RiskLevel).IsRequired();
        builder.Property(ra => ra.RiskScore).HasPrecision(5, 2).IsRequired();
        builder.Property(ra => ra.Factors).HasMaxLength(2000);
        builder.Property(ra => ra.Recommendations).HasMaxLength(2000);
        builder.Property(ra => ra.AssessedAt).IsRequired();
        builder.Property(ra => ra.CreatedAt).IsRequired();
        builder.Property(ra => ra.UpdatedAt);

        builder.HasIndex(ra => ra.CrimeCaseId);
        builder.HasIndex(ra => ra.DistrictId);
        builder.HasIndex(ra => ra.AssessedById);
        builder.HasIndex(ra => ra.RiskLevel);
        builder.HasIndex(ra => ra.AssessedAt);

        // Area-level assessments have no case; case deletion must not remove them.
        builder.HasOne(ra => ra.CrimeCase)
            .WithMany(c => c.RiskAssessments)
            .HasForeignKey(ra => ra.CrimeCaseId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(ra => ra.District)
            .WithMany()
            .HasForeignKey(ra => ra.DistrictId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(ra => ra.AssessedBy)
            .WithMany()
            .HasForeignKey(ra => ra.AssessedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class AnomalyConfiguration : IEntityTypeConfiguration<Anomaly>
{
    public void Configure(EntityTypeBuilder<Anomaly> builder)
    {
        builder.ToTable("Anomalies");

        builder.Property(a => a.Title).HasMaxLength(200).IsRequired();
        builder.Property(a => a.Description).HasMaxLength(4000);
        builder.Property(a => a.Severity).HasConversion<int>().IsRequired();
        builder.Property(a => a.AnomalyType).IsRequired();
        builder.Property(a => a.RelatedEntityType).HasMaxLength(100);
        builder.Property(a => a.DetectedPatterns).HasMaxLength(2000);
        builder.Property(a => a.ConfidenceScore).HasPrecision(5, 4).IsRequired();
        builder.Property(a => a.DetectedAt).IsRequired();
        builder.Property(a => a.Status).IsRequired();
        builder.Property(a => a.CreatedAt).IsRequired();

        builder.HasIndex(a => a.RelatedEntityId);
        builder.HasIndex(a => a.RelatedEntityType);
        builder.HasIndex(a => a.Severity);
        builder.HasIndex(a => a.Status);
        builder.HasIndex(a => a.DetectedAt);
        builder.HasIndex(a => a.DetectedById);
        builder.HasIndex(a => a.ResolvedById);

        builder.HasOne(a => a.DetectedBy)
            .WithMany()
            .HasForeignKey(a => a.DetectedById)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(a => a.ResolvedBy)
            .WithMany()
            .HasForeignKey(a => a.ResolvedById)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public class NetworkEntityConfiguration : IEntityTypeConfiguration<NetworkEntity>
{
    public void Configure(EntityTypeBuilder<NetworkEntity> builder)
    {
        // A NetworkEntity is a reference wrapper, never an independent node record:
        // exactly one of the real NETRA entity foreign keys must be populated.
        builder.ToTable("NetworkEntities", table => table.HasCheckConstraint(
            "CK_NetworkEntities_ExactlyOneReference",
            "(CASE WHEN [PersonId] IS NULL THEN 0 ELSE 1 END + " +
            "CASE WHEN [CrimeCaseId] IS NULL THEN 0 ELSE 1 END + " +
            "CASE WHEN [LocationId] IS NULL THEN 0 ELSE 1 END) = 1"));

        builder.Property(ne => ne.Name).HasMaxLength(200).IsRequired();
        builder.Property(ne => ne.EntityType).IsRequired();
        builder.Property(ne => ne.EntitySubType).HasMaxLength(100);
        builder.Property(ne => ne.Attributes).HasMaxLength(2000);
        builder.Property(ne => ne.CentralityScore).HasPrecision(10, 4);
        builder.Property(ne => ne.RiskLevel).IsRequired();
        builder.Property(ne => ne.CreatedAt).IsRequired();
        builder.Property(ne => ne.UpdatedAt);

        builder.HasIndex(ne => ne.EntityType);
        builder.HasIndex(ne => ne.PersonId);
        builder.HasIndex(ne => ne.CrimeCaseId);
        builder.HasIndex(ne => ne.LocationId);
        builder.HasIndex(ne => ne.IsActive);
        builder.HasIndex(ne => ne.RiskLevel);

        builder.HasOne(ne => ne.Person)
            .WithMany(p => p.NetworkEntities)
            .HasForeignKey(ne => ne.PersonId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(ne => ne.CrimeCase)
            .WithMany()
            .HasForeignKey(ne => ne.CrimeCaseId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(ne => ne.Location)
            .WithMany(l => l.NetworkEntities)
            .HasForeignKey(ne => ne.LocationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(ne => ne.SourceRelationships)
            .WithOne(r => r.SourceEntity)
            .HasForeignKey(r => r.SourceEntityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(ne => ne.TargetRelationships)
            .WithOne(r => r.TargetEntity)
            .HasForeignKey(r => r.TargetEntityId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class NetworkRelationshipConfiguration : IEntityTypeConfiguration<NetworkRelationship>
{
    public void Configure(EntityTypeBuilder<NetworkRelationship> builder)
    {
        builder.ToTable("NetworkRelationships");

        builder.Property(nr => nr.RelationshipType).HasConversion<int>().IsRequired();
        builder.Property(nr => nr.Description).HasMaxLength(500);
        builder.Property(nr => nr.Strength).HasPrecision(5, 2).IsRequired();
        builder.Property(nr => nr.Evidence).HasMaxLength(2000);
        builder.Property(nr => nr.CreatedAt).IsRequired();
        builder.Property(nr => nr.IsActive).IsRequired();

        builder.HasIndex(nr => nr.SourceEntityId);
        builder.HasIndex(nr => nr.TargetEntityId);
        builder.HasIndex(nr => nr.RelationshipType);
        builder.HasIndex(nr => nr.IsActive);
        builder.HasIndex(nr => new { nr.SourceEntityId, nr.TargetEntityId, nr.RelationshipType }).IsUnique();
        builder.HasIndex(nr => nr.CreatedById);

        builder.HasOne(nr => nr.SourceEntity)
            .WithMany(ne => ne.SourceRelationships)
            .HasForeignKey(nr => nr.SourceEntityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(nr => nr.TargetEntity)
            .WithMany(ne => ne.TargetRelationships)
            .HasForeignKey(nr => nr.TargetEntityId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(nr => nr.CreatedBy)
            .WithMany()
            .HasForeignKey(nr => nr.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
