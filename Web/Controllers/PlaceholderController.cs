using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NETRA.Web.Models.Navigation;

namespace NETRA.Web.Controllers;

/// <summary>
/// Renders an honest "not yet implemented" page for modules scheduled for later phases.
/// Placeholder text only — no fabricated analytical data.
/// </summary>
[Authorize]
public class PlaceholderController : Controller
{
    [HttpGet("/investigations")]
    public IActionResult Investigations() => Placeholder("Investigations");

    [HttpGet("/persons")]
    public IActionResult Persons() => Placeholder("Persons");

    [HttpGet("/evidence")]
    public IActionResult Evidence() => Placeholder("Evidence");

    // User Management and Audit Logs became real modules in Phase 13 (/Users and
    // /AuditLogs), and Settings and Support became real modules in Phase 14
    // (/settings/* and /support), so their placeholder routes are gone.

    private IActionResult Placeholder(string action)
    {
        ViewData["ModuleName"] = NetraNavigation.DisplayNameFor(action);
        ViewData["ModuleIcon"] = NetraNavigation.IconFor(action);
        return View("Placeholder");
    }
}
