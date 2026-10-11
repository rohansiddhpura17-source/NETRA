using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NETRA.Domain.Cases;
using NETRA.Domain.Enums;

namespace NETRA.Infrastructure.Data.Configurations;

public class CrimeCaseConfiguration : IEntityTypeConfiguration<CrimeCase>
{
    public void Configure(EntityTypeBuilder<CrimeCase> builder)
    {
        builder.ToTable("CrimeCases");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.CaseNumber).HasMaxLength(50).IsRequired();
        builder.Property(c => c.Title).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(2000);
        builder.Property(c => c.IncidentDate).IsRequired();
        builder.Property(c => c.ReportedDate);
        builder.Property(c => c.ClosedDate);
        builder.Property(c => c.Status).HasConversion<int>().IsRequired();
        builder.Property(c => c.Severity).HasConversion<int>().IsRequired();
        builder.Property(c => c.CreatedAt).IsRequired();
        builder.Property(c => c.UpdatedAt).IsRequired();

        builder.HasIndex(c => c.CaseNumber).IsUnique();
        builder.HasIndex(c => c.IncidentDate);
        builder.HasIndex(c => c.Status);
        builder.HasIndex(c => c.Severity);
        builder.HasIndex(c => c.CrimeTypeId);
        builder.HasIndex(c => c.AssignedInvestigatorId);
        builder.HasIndex(c => c.DepartmentId);
        builder.HasIndex(c => c.PoliceStationId);
        builder.HasIndex(c => c.CreatedById);

        builder.HasOne(c => c.CrimeType)
            .WithMany(ct => ct.Cases)
            .HasForeignKey(c => c.CrimeTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.AssignedInvestigator)
            .WithMany()
            .HasForeignKey(c => c.AssignedInvestigatorId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(c => c.SupervisingOfficer)
            .WithMany()
            .HasForeignKey(c => c.SupervisingOfficerId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(c => c.Department)
            .WithMany()
            .HasForeignKey(c => c.DepartmentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(c => c.CreatedBy)
            .WithMany()
            .HasForeignKey(c => c.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.CasePersons)
            .WithOne(cp => cp.CrimeCase)
            .HasForeignKey(cp => cp.CrimeCaseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.CaseLocations)
            .WithOne(cl => cl.CrimeCase)
            .HasForeignKey(cl => cl.CrimeCaseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.Investigation)
            .WithOne(i => i.CrimeCase)
            .HasForeignKey<NETRA.Domain.Investigation.Investigation>(i => i.CrimeCaseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Alerts)
            .WithOne(a => a.CrimeCase)
            .HasForeignKey(a => a.CrimeCaseId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(c => c.RiskAssessments)
            .WithOne(ra => ra.CrimeCase)
            .HasForeignKey(ra => ra.CrimeCaseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Reports)
            .WithOne(r => r.CrimeCase)
            .HasForeignKey(r => r.CrimeCaseId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(c => c.ImageRecords)
            .WithOne(ir => ir.CrimeCase)
            .HasForeignKey(ir => ir.CrimeCaseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class CrimeTypeConfiguration : IEntityTypeConfiguration<CrimeType>
{
    public void Configure(EntityTypeBuilder<CrimeType> builder)
    {
        builder.ToTable("CrimeTypes");

        builder.Property(ct => ct.Name).HasMaxLength(100).IsRequired();
        builder.Property(ct => ct.Code).HasMaxLength(20).IsRequired();
        builder.Property(ct => ct.Description).HasMaxLength(500);
        builder.Property(ct => ct.CreatedAt).IsRequired();
        builder.Property(ct => ct.DisplayOrder).IsRequired();

        builder.HasIndex(ct => ct.Code).IsUnique();
        builder.HasIndex(ct => ct.ParentCrimeTypeId);
        builder.HasIndex(ct => ct.IsActive);
        builder.HasIndex(ct => ct.DisplayOrder);

        builder.HasOne(ct => ct.ParentCrimeType)
            .WithMany(ct => ct.SubCrimeTypes)
            .HasForeignKey(ct => ct.ParentCrimeTypeId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public class CasePersonConfiguration : IEntityTypeConfiguration<CasePerson>
{
    public void Configure(EntityTypeBuilder<CasePerson> builder)
    {
        builder.ToTable("CasePersons");

        builder.Property(cp => cp.Role).HasConversion<int>().IsRequired();
        builder.Property(cp => cp.Notes).HasMaxLength(500);
        builder.Property(cp => cp.AssociatedAt).IsRequired();

        builder.HasIndex(cp => cp.CrimeCaseId);
        builder.HasIndex(cp => cp.PersonId);
        builder.HasIndex(cp => cp.Role);
        builder.HasIndex(cp => new { cp.CrimeCaseId, cp.PersonId }).IsUnique();

        builder.HasOne(cp => cp.CrimeCase)
            .WithMany(c => c.CasePersons)
            .HasForeignKey(cp => cp.CrimeCaseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(cp => cp.Person)
            .WithMany(p => p.CasePersons)
            .HasForeignKey(cp => cp.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(cp => cp.AssociatedBy)
            .WithMany()
            .HasForeignKey(cp => cp.AssociatedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class CaseLocationConfiguration : IEntityTypeConfiguration<CaseLocation>
{
    public void Configure(EntityTypeBuilder<CaseLocation> builder)
    {
        builder.ToTable("CaseLocations");

        builder.Property(cl => cl.LocationRole).HasMaxLength(100);
        builder.Property(cl => cl.Notes).HasMaxLength(500);
        builder.Property(cl => cl.AssociatedAt).IsRequired();

        builder.HasIndex(cl => cl.CrimeCaseId);
        builder.HasIndex(cl => cl.LocationId);
        builder.HasIndex(cl => new { cl.CrimeCaseId, cl.LocationId }).IsUnique();

        builder.HasOne(cl => cl.CrimeCase)
            .WithMany(c => c.CaseLocations)
            .HasForeignKey(cl => cl.CrimeCaseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(cl => cl.Location)
            .WithMany(l => l.CaseLocations)
            .HasForeignKey(cl => cl.LocationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}