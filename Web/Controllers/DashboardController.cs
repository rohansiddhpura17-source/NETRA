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
    private readonly IPublicCrimeStatsService _publicStats;

    public DashboardController(
        IDashboardService dashboard,
        IPublicCrimeStatsService publicStats)
    {
        _dashboard = dashboard;
        _publicStats = publicStats;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var dto = await _dashboard.GetDashboardAsync(cancellationToken);
        var publicStats = await _publicStats.GetGujaratCybercrimeStatsAsync(cancellationToken);
        return View(DashboardViewModel.Create(dto, publicStats));
    }
}
