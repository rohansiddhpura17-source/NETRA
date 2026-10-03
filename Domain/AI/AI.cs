using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NETRA.Domain.Enums;
using NETRA.Domain.Security;

namespace NETRA.Domain.AI;

public class AssistantSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public virtual ApplicationUser User { get; set; } = null!;

    [MaxLength(200)]
    public string? Title { get; set; }

    public int SessionType { get; set; } = 1;

    [MaxLength(100)]
    public string? ModelName { get; set; }

    [MaxLength(4000)]
    public string? SystemPrompt { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime? EndedAt { get; set; }

    public int Status { get; set; } = 1;

    public int TotalMessages { get; set; } = 0;

    public int TotalTokensUsed { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<AssistantMessage> Messages { get; set; } = new List<AssistantMessage>();
}

public class AssistantMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SessionId { get; set; }

    [ForeignKey(nameof(SessionId))]
    public virtual AssistantSession Session { get; set; } = null!;

    public int Role { get; set; } = 1;

    [MaxLength(8000)]
    public string Content { get; set; } = string.Empty;

    public int TokensUsed { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(4000)]
    public string? Metadata { get; set; }

    public Guid? RelatedEntityId { get; set; }

    [MaxLength(100)]
    public string? RelatedEntityType { get; set; }
}