using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NETRA.Domain.People;

namespace NETRA.Infrastructure.Data.Configurations;

public class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("Persons");

        builder.Property(p => p.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(p => p.MiddleName).HasMaxLength(100);
        builder.Property(p => p.LastName).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Gender).HasMaxLength(20);
        builder.Property(p => p.NationalIdNumber).HasMaxLength(50);
        builder.Property(p => p.PassportNumber).HasMaxLength(50);
        builder.Property(p => p.DriversLicenseNumber).HasMaxLength(50);
        builder.Property(p => p.Email).HasMaxLength(200);
        builder.Property(p => p.PhoneNumber).HasMaxLength(20);
        builder.Property(p => p.Address).HasMaxLength(500);
        builder.Property(p => p.Notes).HasMaxLength(2000);
        builder.Property(p => p.CreatedAt).IsRequired();
        builder.Property(p => p.UpdatedAt).IsRequired();

        builder.HasIndex(p => p.LastName);
        builder.HasIndex(p => p.FirstName);
        builder.HasIndex(p => p.Email);
        builder.HasIndex(p => p.NationalIdNumber).IsUnique().HasFilter("[NationalIdNumber] IS NOT NULL");
        builder.HasIndex(p => p.PassportNumber).IsUnique().HasFilter("[PassportNumber] IS NOT NULL");
        builder.HasIndex(p => p.IsActive);
        builder.HasIndex(p => p.CreatedById);

        builder.HasOne(p => p.CreatedBy)
            .WithMany()
            .HasForeignKey(p => p.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(p => p.Aliases)
            .WithOne(a => a.Person)
            .HasForeignKey(a => a.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.PersonLocations)
            .WithOne(pl => pl.Person)
            .HasForeignKey(pl => pl.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.CasePersons)
            .WithOne(cp => cp.Person)
            .HasForeignKey(cp => cp.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.NetworkEntities)
            .WithOne(ne => ne.Person)
            .HasForeignKey(ne => ne.PersonId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(p => p.EvidenceItems)
            .WithOne(e => e.Person)
            .HasForeignKey(e => e.PersonId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class PersonAliasConfiguration : IEntityTypeConfiguration<PersonAlias>
{
    public void Configure(EntityTypeBuilder<PersonAlias> builder)
    {
        builder.ToTable("PersonAliases");

        builder.Property(a => a.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(a => a.MiddleName).HasMaxLength(100);
        builder.Property(a => a.LastName).HasMaxLength(100).IsRequired();
        builder.Property(a => a.AliasType).HasMaxLength(100);
        builder.Property(a => a.CreatedAt).IsRequired();

        builder.HasIndex(a => a.PersonId);
        builder.HasIndex(a => a.IsPrimaryAlias);
        builder.HasIndex(a => new { a.PersonId, a.IsPrimaryAlias })
            .IsUnique()
            .HasFilter("[IsPrimaryAlias] = 1");

        builder.HasOne(a => a.Person)
            .WithMany(p => p.Aliases)
            .HasForeignKey(a => a.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.CreatedBy)
            .WithMany()
            .HasForeignKey(a => a.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class PersonLocationConfiguration : IEntityTypeConfiguration<PersonLocation>
{
    public void Configure(EntityTypeBuilder<PersonLocation> builder)
    {
        builder.ToTable("PersonLocations");

        builder.Property(pl => pl.LocationType).HasMaxLength(100);
        builder.Property(pl => pl.Notes).HasMaxLength(500);
        builder.Property(pl => pl.CreatedAt).IsRequired();

        builder.HasIndex(pl => pl.PersonId);
        builder.HasIndex(pl => pl.LocationId);
        builder.HasIndex(pl => new { pl.PersonId, pl.LocationId, pl.FromDate }).IsUnique();

        builder.HasOne(pl => pl.Person)
            .WithMany(p => p.PersonLocations)
            .HasForeignKey(pl => pl.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(pl => pl.Location)
            .WithMany(l => l.PersonLocations)
            .HasForeignKey(pl => pl.LocationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}