using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NETRA.Domain.Alerts;
using NETRA.Domain.Enums;
using NETRA.Domain.Cases;
using NETRA.Domain.Security;

namespace NETRA.Infrastructure.Data.Configurations;

public class AlertConfiguration : IEntityTypeConfiguration<Alert>
{
    public void Configure(EntityTypeBuilder<Alert> builder)
    {
        builder.ToTable("Alerts");

        builder.Property(a => a.Title).HasMaxLength(200).IsRequired();
        builder.Property(a => a.Description).HasMaxLength(2000);
        builder.Property(a => a.Severity).HasConversion<int>().IsRequired();
        builder.Property(a => a.Status).HasConversion<int>().IsRequired();
        builder.Property(a => a.AlertType).IsRequired();
        builder.Property(a => a.RelatedEntityType).HasMaxLength(100);
        builder.Property(a => a.ResolutionNotes).HasMaxLength(2000);
        builder.Property(a => a.AlertKey).HasMaxLength(160);
        builder.Property(a => a.CreatedAt).IsRequired();
        builder.Property(a => a.AcknowledgedAt);
        builder.Property(a => a.ResolvedAt);

        builder.HasIndex(a => a.CrimeCaseId);
        builder.HasIndex(a => a.Status);
        builder.HasIndex(a => a.Severity);
        builder.HasIndex(a => a.AssignedToId);
        builder.HasIndex(a => a.CreatedAt);
        builder.HasIndex(a => a.RelatedEntityId);
        builder.HasIndex(a => a.RelatedEntityType);

        // Idempotent alert generation: one generated alert per stable key
        // (category + source + analysis window). Manual alerts keep a null key.
        builder.HasIndex(a => a.AlertKey).IsUnique().HasFilter("[AlertKey] IS NOT NULL");

        builder.HasOne(a => a.CrimeCase)
            .WithMany(c => c.Alerts)
            .HasForeignKey(a => a.CrimeCaseId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(a => a.AssignedTo)
            .WithMany()
            .HasForeignKey(a => a.AssignedToId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(a => a.ResolvedBy)
            .WithMany()
            .HasForeignKey(a => a.ResolvedById)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(a => a.Notifications)
            .WithOne(n => n.Alert)
            .HasForeignKey(n => n.AlertId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");

        builder.Property(n => n.Title).HasMaxLength(200).IsRequired();
        builder.Property(n => n.Message).HasMaxLength(2000);
        builder.Property(n => n.NotificationType).IsRequired();
        builder.Property(n => n.DeliveryChannel).IsRequired();
        builder.Property(n => n.IsRead).IsRequired();
        builder.Property(n => n.IsSent).IsRequired();
        builder.Property(n => n.CreatedAt).IsRequired();
        builder.Property(n => n.CorrelationId).HasMaxLength(100);

        builder.HasIndex(n => n.AlertId);
        builder.HasIndex(n => n.RecipientId);
        builder.HasIndex(n => n.IsRead);
        builder.HasIndex(n => n.IsSent);
        builder.HasIndex(n => n.CreatedAt);
        builder.HasIndex(n => n.CorrelationId);

        builder.HasOne(n => n.Alert)
            .WithMany(a => a.Notifications)
            .HasForeignKey(n => n.AlertId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(n => n.Recipient)
            .WithMany(u => u.Notifications)
            .HasForeignKey(n => n.RecipientId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}