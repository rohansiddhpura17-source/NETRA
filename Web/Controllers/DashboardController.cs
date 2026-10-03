using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NETRA.Application.Dashboard;
using NETRA.Web.Authorization;
using NETRA.Web.Models.ViewModels.Dashboard;

namespace NETRA.Web.Controllers;

[Authorize(Policy = NetraPolicies.CaseRead)]
public class DashboardController : Controller
{
    private readonly IDashboardService _dashboard;

    public DashboardController(IDashboardService dashboard)
    {
        _dashboard = dashboard;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var dto = await _dashboard.GetDashboardAsync(cancellationToken);
        return View(DashboardViewModel.Create(dto));
    }
}
