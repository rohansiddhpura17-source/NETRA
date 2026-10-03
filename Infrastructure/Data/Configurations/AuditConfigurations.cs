using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NETRA.Domain.Audit;
using NETRA.Domain.Security;

namespace NETRA.Infrastructure.Data.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");

        builder.Property(al => al.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(al => al.Action).HasMaxLength(50).IsRequired();
        builder.Property(al => al.OldValues).HasMaxLength(4000);
        builder.Property(al => al.NewValues).HasMaxLength(4000);
        builder.Property(al => al.PropertyName).HasMaxLength(200);
        builder.Property(al => al.Timestamp).IsRequired();
        builder.Property(al => al.IpAddress).HasMaxLength(100);
        builder.Property(al => al.UserAgent).HasMaxLength(500);
        builder.Property(al => al.CorrelationId).HasMaxLength(100);
        builder.Property(al => al.SessionId).HasMaxLength(100);
        builder.Property(al => al.IsSystemAction).IsRequired();

        builder.HasIndex(al => al.EntityType);
        builder.HasIndex(al => al.EntityId);
        builder.HasIndex(al => al.UserId);
        builder.HasIndex(al => al.Timestamp);
        builder.HasIndex(al => al.CorrelationId);
        builder.HasIndex(al => al.SessionId);
        builder.HasIndex(al => new { al.EntityType, al.EntityId, al.Timestamp });

        builder.HasOne(al => al.User)
            .WithMany(u => u.AuditLogs)
            .HasForeignKey(al => al.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}