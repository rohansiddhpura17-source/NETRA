using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NETRA.Application.Abstractions;
using NETRA.Application.CaseOperations;
using NETRA.Domain.Enums;
using NETRA.Web.Authorization;
using NETRA.Web.Models.ViewModels.Cases;

namespace NETRA.Web.Controllers;

/// <summary>
/// Investigation workspace. Presents the investigation attached to a case, split into
/// the panels required in Phase 4.
/// </summary>
[Authorize]
[Route("Investigations")]
public class InvestigationsController : Controller
{
    private readonly ICaseService _cases;
    private readonly IAuditActorAccessor _actor;
    private readonly IAuthorizationService _authorization;

    public InvestigationsController(
        ICaseService cases,
        IAuditActorAccessor actor,
        IAuthorizationService authorization)
    {
        _cases = cases;
        _actor = actor;
        _authorization = authorization;
    }

    /// <summary>
    /// Resolves the workspace by investigation id. Falls back to a case id so the URL
    /// works whether the caller holds one or the other.
    /// </summary>
    [HttpGet("Details/{id:guid}")]
    [Authorize(Policy = NetraPolicies.CaseRead)]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        // The URL is /Investigations/Details/{id}, so resolve by investigation id first
        // and fall back to treating the id as a case id.
        var detail = await _cases.GetCaseByInvestigationIdAsync(id, cancellationToken)
                     ?? await _cases.GetCaseDetailsAsync(id, cancellationToken);

        if (detail is null)
        {
            return NotFound();
        }

        if (detail.InvestigationId is not { } investigationId)
        {
            TempData["Netra.Error"] = "This case has no investigation workspace yet.";
            return RedirectToAction("Details", "Cases", new { id = detail.Id });
        }

        var history = await _cases.GetCaseHistoryAsync(detail.Id, cancellationToken);

        var model = new InvestigationWorkspaceViewModel
        {
            InvestigationId = investigationId,
            CaseId = detail.Id,
            Case = detail,
            History = history,
            Victims = detail.Persons.Where(p => p.Role == nameof(PersonRole.Victim)).ToList(),
            Suspects = detail.Persons.Where(p => p.Role == nameof(PersonRole.Suspect)).ToList(),
            PersonsOfInterest = detail.Persons.Where(p => p.Role == nameof(PersonRole.PersonOfInterest)).ToList(),
            OtherPersons = detail.Persons.Where(p =>
                p.Role is not (nameof(PersonRole.Victim) or nameof(PersonRole.Suspect)
                    or nameof(PersonRole.PersonOfInterest))).ToList(),
            SceneLocations = detail.Locations
                .Where(l => l.LocationRole == "Incident")
                .ToList(),
            RelatedLocations = detail.Locations
                .Where(l => l.LocationRole != "Incident")
                .ToList(),
            CanEdit = (await _authorization.AuthorizeAsync(User, NetraPolicies.CaseEdit)).Succeeded,
            CanManageEvidence = (await _authorization.AuthorizeAsync(User, NetraPolicies.CaseManageEvidence)).Succeeded,
            CanManageNotes = (await _authorization.AuthorizeAsync(User, NetraPolicies.CaseManageNotes)).Succeeded,
            CanManageTimeline = (await _authorization.AuthorizeAsync(User, NetraPolicies.CaseManageTimeline)).Succeeded
        };

        ViewBag.CaseId = detail.Id;
        ViewBag.CaseNumber = detail.CaseNumber;

        return View(model);
    }
}
