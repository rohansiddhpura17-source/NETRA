using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NETRA.Domain.Security;

namespace NETRA.Infrastructure.Data.Configurations;

public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.ToTable("ApplicationUsers");

        builder.Property(u => u.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.LastName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.BadgeNumber).HasMaxLength(50);
        builder.Property(u => u.Rank).HasMaxLength(100);
        builder.Property(u => u.CreatedAt).IsRequired();
        builder.Property(u => u.LastLoginAt);

        builder.HasIndex(u => u.BadgeNumber).IsUnique().HasFilter("[BadgeNumber] IS NOT NULL");
        builder.HasIndex(u => u.DepartmentId);
        builder.HasIndex(u => u.PoliceStationId);
        builder.HasIndex(u => u.IsActive);

        builder.HasOne(u => u.Department)
            .WithMany(d => d.Users)
            .HasForeignKey(u => u.DepartmentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(u => u.UserSettings)
            .WithOne()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(u => u.NotificationPreferences)
            .WithOne()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(u => u.AuditLogs)
            .WithOne(a => a.User)
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(u => u.Notifications)
            .WithOne(n => n.Recipient)
            .HasForeignKey(n => n.RecipientId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(u => u.AssistantSessions)
            .WithOne(s => s.User)
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("Departments");

        builder.Property(d => d.Name).HasMaxLength(100).IsRequired();
        builder.Property(d => d.Code).HasMaxLength(20);
        builder.Property(d => d.Description).HasMaxLength(500);
        builder.Property(d => d.CreatedAt).IsRequired();

        builder.HasIndex(d => d.Code).IsUnique().HasFilter("[Code] IS NOT NULL");
        builder.HasIndex(d => d.ParentDepartmentId);
        builder.HasIndex(d => d.IsActive);

        builder.HasOne(d => d.ParentDepartment)
            .WithMany(d => d.SubDepartments)
            .HasForeignKey(d => d.ParentDepartmentId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}