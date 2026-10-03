using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NETRA.Domain.AI;
using NETRA.Domain.Security;

namespace NETRA.Infrastructure.Data.Configurations;

public class AssistantSessionConfiguration : IEntityTypeConfiguration<AssistantSession>
{
    public void Configure(EntityTypeBuilder<AssistantSession> builder)
    {
        builder.ToTable("AssistantSessions");

        builder.Property(s => s.Title).HasMaxLength(200);
        builder.Property(s => s.SessionType).IsRequired();
        builder.Property(s => s.ModelName).HasMaxLength(100);
        builder.Property(s => s.SystemPrompt).HasMaxLength(4000);
        builder.Property(s => s.StartedAt).IsRequired();
        builder.Property(s => s.EndedAt);
        builder.Property(s => s.Status).IsRequired();
        builder.Property(s => s.TotalMessages).IsRequired();
        builder.Property(s => s.TotalTokensUsed).IsRequired();
        builder.Property(s => s.CreatedAt).IsRequired();

        builder.HasIndex(s => s.UserId);
        builder.HasIndex(s => s.Status);
        builder.HasIndex(s => s.StartedAt);

        builder.HasOne(s => s.User)
            .WithMany(u => u.AssistantSessions)
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(s => s.Messages)
            .WithOne(m => m.Session)
            .HasForeignKey(m => m.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class AssistantMessageConfiguration : IEntityTypeConfiguration<AssistantMessage>
{
    public void Configure(EntityTypeBuilder<AssistantMessage> builder)
    {
        builder.ToTable("AssistantMessages");

        builder.Property(m => m.Role).IsRequired();
        builder.Property(m => m.Content).HasMaxLength(8000).IsRequired();
        builder.Property(m => m.TokensUsed).IsRequired();
        builder.Property(m => m.CreatedAt).IsRequired();
        builder.Property(m => m.Metadata).HasMaxLength(4000);
        builder.Property(m => m.RelatedEntityType).HasMaxLength(100);

        builder.HasIndex(m => m.SessionId);
        builder.HasIndex(m => m.CreatedAt);
        builder.HasIndex(m => m.Role);
        builder.HasIndex(m => m.RelatedEntityId);

        builder.HasOne(m => m.Session)
            .WithMany(s => s.Messages)
            .HasForeignKey(m => m.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}