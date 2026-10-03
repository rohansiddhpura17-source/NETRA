using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NETRA.Domain.Settings;
using NETRA.Domain.Security;

namespace NETRA.Infrastructure.Data.Configurations;

public class UserSettingConfiguration : IEntityTypeConfiguration<UserSetting>
{
    public void Configure(EntityTypeBuilder<UserSetting> builder)
    {
        builder.ToTable("UserSettings");

        builder.Property(s => s.Key).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Value).HasMaxLength(4000);
        builder.Property(s => s.Category).HasMaxLength(100);
        builder.Property(s => s.Description).HasMaxLength(500);
        builder.Property(s => s.CreatedAt).IsRequired();
        builder.Property(s => s.UpdatedAt).IsRequired();

        builder.HasIndex(s => s.UserId);
        builder.HasIndex(s => s.Key);
        builder.HasIndex(s => s.Category);
        builder.HasIndex(s => new { s.UserId, s.Key }).IsUnique();

        builder.HasOne(s => s.User)
            .WithMany(u => u.UserSettings)
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class SecuritySettingConfiguration : IEntityTypeConfiguration<SecuritySetting>
{
    public void Configure(EntityTypeBuilder<SecuritySetting> builder)
    {
        builder.ToTable("SecuritySettings");

        builder.Property(s => s.Key).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Value).HasMaxLength(4000).IsRequired();
        builder.Property(s => s.Category).HasMaxLength(100);
        builder.Property(s => s.Description).HasMaxLength(500);
        builder.Property(s => s.CreatedAt).IsRequired();
        builder.Property(s => s.UpdatedAt).IsRequired();

        builder.HasIndex(s => s.Key).IsUnique();
        builder.HasIndex(s => s.Category);
        builder.HasIndex(s => s.IsSystemSetting);
        builder.HasIndex(s => s.UpdatedById);

        builder.HasOne(s => s.UpdatedBy)
            .WithMany()
            .HasForeignKey(s => s.UpdatedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("NotificationPreferences");

        builder.Property(np => np.NotificationType).IsRequired();
        builder.Property(np => np.DeliveryChannel).IsRequired();
        builder.Property(np => np.IsEnabled).IsRequired();
        builder.Property(np => np.Filters).HasMaxLength(2000);
        builder.Property(np => np.CreatedAt).IsRequired();
        builder.Property(np => np.UpdatedAt).IsRequired();

        builder.HasIndex(np => np.UserId);
        builder.HasIndex(np => np.NotificationType);
        builder.HasIndex(np => new { np.UserId, np.NotificationType, np.DeliveryChannel }).IsUnique();

        builder.HasOne(np => np.User)
            .WithMany(u => u.NotificationPreferences)
            .HasForeignKey(np => np.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class SystemPreferenceConfiguration : IEntityTypeConfiguration<SystemPreference>
{
    public void Configure(EntityTypeBuilder<SystemPreference> builder)
    {
        builder.ToTable("SystemPreferences");

        builder.Property(sp => sp.Key).HasMaxLength(100).IsRequired();
        builder.Property(sp => sp.Value).HasMaxLength(4000).IsRequired();
        builder.Property(sp => sp.Category).HasMaxLength(100);
        builder.Property(sp => sp.Description).HasMaxLength(500);
        builder.Property(sp => sp.ValueType).IsRequired();
        builder.Property(sp => sp.IsReadOnly).IsRequired();
        builder.Property(sp => sp.CreatedAt).IsRequired();
        builder.Property(sp => sp.UpdatedAt).IsRequired();

        builder.HasIndex(sp => sp.Key).IsUnique();
        builder.HasIndex(sp => sp.Category);
        builder.HasIndex(sp => sp.UpdatedById);

        builder.HasOne(sp => sp.UpdatedBy)
            .WithMany()
            .HasForeignKey(sp => sp.UpdatedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}