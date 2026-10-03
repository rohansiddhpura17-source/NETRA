using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NETRA.Application.Abstractions;
using NETRA.Application.Alerts;
using NETRA.Domain.Enums;
using NETRA.Web.Authorization;
using NETRA.Web.Models.ViewModels.Alerts;

namespace NETRA.Web.Controllers;

/// <summary>
/// Alerts &amp; Notifications module (netra.pdf page 12). The controller only
/// translates requests into service calls and operation results into redirect
/// messages — all querying, filtering and generation lives in the services.
/// </summary>
[Authorize(Policy = NetraPolicies.AlertsRead)]
public class AlertsController : Controller
{
    private readonly IAlertService _alerts;
    private readonly IAuditActorAccessor _actor;

    public AlertsController(IAlertService alerts, IAuditActorAccessor actor)
    {
        _alerts = alerts;
        _actor = actor;
    }

    [HttpGet("/alerts")]
    public async Task<IActionResult> Index(
        AlertsFilterViewModel filter,
        CancellationToken cancellationToken)
    {
        var paged = await _alerts.ListAsync(filter.ToCriteria(), cancellationToken);
        var summary = await _alerts.GetSummaryAsync(cancellationToken);
        var caseOptions = await _alerts.GetCaseOptionsAsync(cancellationToken);

        return View(AlertsIndexViewModel.Create(
            paged, summary, filter, caseOptions, CanManage()));
    }

    [HttpGet("/alerts/details/{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var detail = await _alerts.GetDetailAsync(id, _actor.GetUserId(User), cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        return View(AlertDetailsViewModel.Create(detail, CanManage()));
    }

    /// <summary>
    /// Explicit, authorised generation pass. Alerts are never generated while
    /// browsing; this is the only trigger, so a page view can never create rows.
    /// </summary>
    [HttpPost("/alerts/generate")]
    [Authorize(Policy = NetraPolicies.AlertsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(
        int windowDays = AlertLimits.DefaultGenerationWindowDays,
        CancellationToken cancellationToken = default)
    {
        var result = await _alerts.GenerateAsync(windowDays, _actor.GetUserId(User), cancellationToken);

        TempData["Netra.Success"] =
            $"Generation pass complete ({result.WindowStart:dd MMM} – {result.WindowEnd:dd MMM}): " +
            $"{result.CreatedCount} new alert{(result.CreatedCount == 1 ? "" : "s")} created, " +
            $"{result.SkippedDuplicateCount} duplicate{(result.SkippedDuplicateCount == 1 ? "" : "s")} skipped.";

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/alerts/acknowledge/{id:guid}")]
    [Authorize(Policy = NetraPolicies.AlertsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Acknowledge(Guid id, CancellationToken cancellationToken)
    {
        return await TransitionAsync(
            id, () => _alerts.AcknowledgeAsync(id, _actor.GetUserId(User), cancellationToken));
    }

    [HttpPost("/alerts/resolve/{id:guid}")]
    [Authorize(Policy = NetraPolicies.AlertsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Resolve(
        Guid id, string? resolutionNotes, CancellationToken cancellationToken)
    {
        return await TransitionAsync(
            id, () => _alerts.ResolveAsync(id, resolutionNotes, _actor.GetUserId(User), cancellationToken));
    }

    [HttpPost("/alerts/dismiss/{id:guid}")]
    [Authorize(Policy = NetraPolicies.AlertsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Dismiss(
        Guid id, string? reason, CancellationToken cancellationToken)
    {
        return await TransitionAsync(
            id, () => _alerts.DismissAsync(id, reason, _actor.GetUserId(User), cancellationToken));
    }

    private async Task<IActionResult> TransitionAsync(Guid id, Func<Task<OperationResult>> action)
    {
        var result = await action();

        switch (result.Status)
        {
            case OperationStatus.NotFound:
                TempData["Netra.Error"] = FirstError(result);
                return RedirectToAction(nameof(Index));

            case OperationStatus.Conflict:
            case OperationStatus.ValidationFailed:
                TempData["Netra.Error"] = FirstError(result);
                return RedirectToAction(nameof(Details), new { id });

            case OperationStatus.Forbidden:
                TempData["Netra.Error"] = FirstError(result);
                return RedirectToAction(nameof(Details), new { id });

            default:
                TempData["Netra.Success"] = "Alert updated.";
                return RedirectToAction(nameof(Details), new { id });
        }
    }

    private static string FirstError(OperationResult result) =>
        result.Errors.Values.SelectMany(e => e).FirstOrDefault() ?? "The alert could not be updated.";

    private bool CanManage() =>
        User.IsInRole(NetraRoles.SystemAdministrator)
        || User.IsInRole(NetraRoles.SeniorOfficer)
        || User.IsInRole(NetraRoles.PoliceIntelligenceAnalyst);
}
