using System.Security.Claims;

namespace NETRA.Application.Abstractions;

/// <summary>
/// Resolves the acting user so audit records are attributed to a real principal.
/// </summary>
public interface IAuditActorAccessor
{
    /// <summary>Authenticated user id, or <see cref="Guid.Empty"/> when anonymous.</summary>
    Guid GetUserId();

    /// <summary>Claim-based lookup used when no service supplied an explicit actor.</summary>
    Guid GetUserId(ClaimsPrincipal? principal);
}
