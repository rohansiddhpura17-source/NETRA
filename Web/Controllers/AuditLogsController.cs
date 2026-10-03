using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NETRA.Application.Audit;
using NETRA.Web.Authorization;
using NETRA.Web.Models.ViewModels.AuditLogs;

namespace NETRA.Web.Controllers;

/// <summary>
/// Audit Log reader (Administration). Reading only: nothing here writes to the trail,
/// and all filtering, paging and projection lives in <see cref="IAuditLogQueryService"/>.
/// Authorisation is the <see cref="NetraPolicies.AuditRead"/> policy, which is a smaller
/// role group than the whole authenticated user base.
/// </summary>
[Authorize(Policy = NetraPolicies.AuditRead)]
public class AuditLogsController : Controller
{
    private readonly IAuditLogQueryService _auditLogs;

    public AuditLogsController(IAuditLogQueryService auditLogs)
    {
        _auditLogs = auditLogs;
    }

    [HttpGet("/AuditLogs")]
    public async Task<IActionResult> Index(
        // Pinned to the query string: a complex model bound from all value providers reads
        // the ambient route values first, and this filter has an Action property that would
        // otherwise silently take the route's action name ("Index") instead of the chosen
        // audit action, emptying the trail.
        [FromQuery] AuditLogFilterViewModel filter,
        CancellationToken cancellationToken)
    {
        var paged = await _auditLogs.ListAsync(filter.ToCriteria(), cancellationToken);
        var options = await _auditLogs.GetFilterOptionsAsync(cancellationToken);

        return View(AuditLogsIndexViewModel.Create(paged, filter, options));
    }

    [HttpGet("/AuditLogs/Details/{id:guid}")]
    public async Task<IActionResult> Details(
        [FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var detail = await _auditLogs.GetDetailAsync(id, cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        return View(new AuditLogDetailsViewModel { Entry = detail });
    }
}
