using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NETRA.Application.Analytics;
using NETRA.Web.Authorization;
using NETRA.Web.Models.ViewModels.Analytics;

namespace NETRA.Web.Controllers;

[Authorize(Policy = NetraPolicies.AnalyticsRead)]
public class AnalyticsController : Controller
{
    private readonly IAnalyticsService _analytics;

    public AnalyticsController(IAnalyticsService analytics)
    {
        _analytics = analytics;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        AnalyticsFilterViewModel filter,
        CancellationToken cancellationToken)
    {
        AnalyticsReportDto report;
        try
        {
            report = await _analytics.GetReportAsync(filter.ToCriteria(), cancellationToken);
        }
        catch (ArgumentException)
        {
            ModelState.AddModelError(nameof(filter.DateTo),
                "The start date must be on or before the end date.");

            // Fall back to the service default window; the submitted filter values are
            // preserved so the user can correct the dates.
            report = await _analytics.GetReportAsync(new AnalyticsFilterCriteria(), cancellationToken);
        }

        var lookups = await _analytics.GetLookupsAsync(cancellationToken);
        return View(AnalyticsViewModel.Create(report, filter, lookups));
    }
}
