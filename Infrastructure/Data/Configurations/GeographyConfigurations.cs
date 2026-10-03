using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NETRA.Domain.Geography;
using NETRA.Domain.Security;

namespace NETRA.Infrastructure.Data.Configurations;

public class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.ToTable("Locations");

        builder.Property(l => l.Name).HasMaxLength(200).IsRequired();
        builder.Property(l => l.Type).HasMaxLength(100);
        builder.Property(l => l.Address).HasMaxLength(500);
        builder.Property(l => l.PostalCode).HasMaxLength(20);
        builder.Property(l => l.CreatedAt).IsRequired();
        builder.Property(l => l.UpdatedAt);

        builder.HasIndex(l => l.Name);
        builder.HasIndex(l => l.Type);
        builder.HasIndex(l => l.DistrictId);
        builder.HasIndex(l => l.PoliceStationId);
        builder.HasIndex(l => l.ParentLocationId);
        builder.HasIndex(l => l.IsActive);
        builder.HasIndex(l => new { l.Latitude, l.Longitude });

        builder.HasOne(l => l.District)
            .WithMany(d => d.Locations)
            .HasForeignKey(l => l.DistrictId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(l => l.PoliceStation)
            .WithMany(ps => ps.Locations)
            .HasForeignKey(l => l.PoliceStationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(l => l.ParentLocation)
            .WithMany(l => l.ChildLocations)
            .HasForeignKey(l => l.ParentLocationId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class DistrictConfiguration : IEntityTypeConfiguration<District>
{
    public void Configure(EntityTypeBuilder<District> builder)
    {
        builder.ToTable("Districts");

        builder.Property(d => d.Name).HasMaxLength(100).IsRequired();
        builder.Property(d => d.Code).HasMaxLength(20).IsRequired();
        builder.Property(d => d.Description).HasMaxLength(500);
        builder.Property(d => d.CreatedAt).IsRequired();

        builder.HasIndex(d => d.Code).IsUnique();
        builder.HasIndex(d => d.IsActive);
        builder.HasIndex(d => new { d.CenterLatitude, d.CenterLongitude });
    }
}

public class PoliceStationConfiguration : IEntityTypeConfiguration<PoliceStation>
{
    public void Configure(EntityTypeBuilder<PoliceStation> builder)
    {
        builder.ToTable("PoliceStations");

        builder.Property(ps => ps.Name).HasMaxLength(100).IsRequired();
        builder.Property(ps => ps.Code).HasMaxLength(20).IsRequired();
        builder.Property(ps => ps.Address).HasMaxLength(500);
        builder.Property(ps => ps.PhoneNumber).HasMaxLength(20);
        builder.Property(ps => ps.Email).HasMaxLength(100);
        builder.Property(ps => ps.CreatedAt).IsRequired();

        builder.HasIndex(ps => ps.Code).IsUnique();
        builder.HasIndex(ps => ps.DistrictId);
        builder.HasIndex(ps => ps.CommandingOfficerId);
        builder.HasIndex(ps => ps.IsActive);
        builder.HasIndex(ps => new { ps.Latitude, ps.Longitude });

        builder.HasOne(ps => ps.District)
            .WithMany(d => d.PoliceStations)
            .HasForeignKey(ps => ps.DistrictId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(ps => ps.CommandingOfficer)
            .WithMany()
            .HasForeignKey(ps => ps.CommandingOfficerId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}