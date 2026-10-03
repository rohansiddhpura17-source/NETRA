using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NETRA.Application.Abstractions;
using NETRA.Domain.Audit;
using NETRA.Infrastructure.Data;

namespace NETRA.Infrastructure.Services;

/// <summary>
/// Writes audit entries through the ambient <see cref="NetraDbContext"/>. Entries are
/// tracked by EF Core and committed with the next SaveChanges, so an audit record and
/// the change it describes are written in one transaction.
/// </summary>
public sealed class AuditService : IAuditService
{
    private readonly NetraDbContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuditActorAccessor _actorAccessor;

    // AuditLogs column widths from Infrastructure/Data/Configurations/AuditConfigurations.cs.
    private const int MaxIpLength = 100;
    private const int MaxUserAgentLength = 500;
    private const int MaxCorrelationLength = 100;

    public AuditService(
        NetraDbContext context,
        IHttpContextAccessor httpContextAccessor,
        IAuditActorAccessor actorAccessor)
    {
        _context = context;
        _httpContextAccessor = httpContextAccessor;
        _actorAccessor = actorAccessor;
    }

    public void Record(
        string entityType,
        Guid entityId,
        string action,
        Guid userId,
        string? propertyName = null,
        string? oldValue = null,
        string? newValue = null,
        string? correlationId = null,
        string? ipAddress = null,
        string? userAgent = null)
    {
        var request = _httpContextAccessor.HttpContext?.Request;

        _context.AuditLogs.Add(new AuditLog
        {
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            PropertyName = propertyName,
            OldValues = Truncate(oldValue),
            NewValues = Truncate(newValue),
            Timestamp = DateTime.UtcNow,
            UserId = userId,
            IpAddress = TruncateColumn(ipAddress ?? request?.Headers["X-Forwarded-For"].FirstOrDefault(), MaxIpLength),
            UserAgent = TruncateColumn(userAgent ?? request?.Headers.UserAgent.ToString(), MaxUserAgentLength),
            CorrelationId = TruncateColumn(correlationId ?? _httpContextAccessor.HttpContext?.TraceIdentifier, MaxCorrelationLength)
        });
    }

    public void RecordSecurityEvent(
        string action,
        Guid userId,
        string? detail = null,
        bool succeeded = true,
        string? correlationId = null,
        string? ipAddress = null)
    {
        var request = _httpContextAccessor.HttpContext?.Request;

        _context.AuditLogs.Add(new AuditLog
        {
            EntityType = AuditEntityTypeNames.Account,
            EntityId = userId,
            Action = action,
            NewValues = Truncate(detail),
            Timestamp = DateTime.UtcNow,
            UserId = userId,
            IpAddress = TruncateColumn(ipAddress ?? request?.Headers["X-Forwarded-For"].FirstOrDefault(), MaxIpLength),
            UserAgent = TruncateColumn(request?.Headers.UserAgent.ToString(), MaxUserAgentLength),
            CorrelationId = TruncateColumn(correlationId ?? _httpContextAccessor.HttpContext?.TraceIdentifier, MaxCorrelationLength)
        });
    }

    /// <summary>AuditLog columns are capped, so long payloads are trimmed, not rejected.</summary>
    private static string? Truncate(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        const int maxLength = 4000;
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    /// <summary>
    /// Trims a caller- or header-supplied value to its column width. The audit trail records
    /// what a request actually sent, and SQL Server rejects an over-long value with error 2628
    /// — which would fail the SaveChanges carrying the audited change itself. InMemory never
    /// enforces the width, so only a real server turns an unusual User-Agent or a long
    /// X-Forwarded-For chain into a 500.
    /// </summary>
    private static string? TruncateColumn(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}


public sealed class HttpContextAuditActorAccessor : IAuditActorAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextAuditActorAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid GetUserId() => GetUserId(_httpContextAccessor.HttpContext?.User);

    public Guid GetUserId(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return Guid.Empty;
        }

        var raw = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? principal.FindFirst("sub")?.Value;

        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }
}