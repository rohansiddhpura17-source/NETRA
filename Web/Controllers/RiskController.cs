using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NETRA.Application.Analytics;
using NETRA.Application.Risk;
using NETRA.Web.Authorization;
using NETRA.Web.Models.ViewModels.Analytics;
using NETRA.Web.Models.ViewModels.Risk;
using NETRA.Web.Services.Charts;

namespace NETRA.Web.Controllers;

/// <summary>
/// Risk &amp; Anomaly Analysis. Indicators are analytical decision-support
/// measurements computed from recorded case data — never predictions.
/// </summary>
[Authorize(Policy = NetraPolicies.RiskRead)]
public class RiskController : Controller
{
    private readonly IRiskAnalysisService _risk;
    private readonly IAnalyticsService _analytics;
    private readonly SvgSparklineRenderer _sparklines;

    public RiskController(
        IRiskAnalysisService risk,
        IAnalyticsService analytics,
        SvgSparklineRenderer sparklines)
    {
        _risk = risk;
        _analytics = analytics;
        _sparklines = sparklines;
    }

    [HttpGet("/risk")]
    public async Task<IActionResult> Index(
        AnalyticsFilterViewModel filter,
        CancellationToken cancellationToken)
    {
        RiskAnalysisDto analysis;
        var criteria = filter.ToCriteria();

        try
        {
            analysis = await _risk.GetAnalysisAsync(criteria, cancellationToken);
        }
        catch (ArgumentException)
        {
            ModelState.AddModelError(nameof(filter.DateTo),
                "The start date must be on or before the end date.");
            analysis = await _risk.GetAnalysisAsync(new AnalyticsFilterCriteria(), cancellationToken);
        }

        var lookups = await _analytics.GetLookupsAsync(cancellationToken);
        return View(RiskViewModel.Create(analysis, filter, lookups, _sparklines));
    }

    /// <summary>
    /// Recomputes indicators and persists area assessments and anomaly records.
    /// Mutations only ever happen here (POST, antiforgery-validated), never on GET.
    /// </summary>
    [HttpPost("/risk/assess")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assess(
        AnalyticsFilterViewModel filter,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _risk.RunAssessmentAsync(filter.ToCriteria(), cancellationToken);

            TempData["Netra.Success"] =
                $"Risk assessment run: {result.AssessmentsSaved} area indicator" +
                $"{(result.AssessmentsSaved == 1 ? "" : "s")} recorded and " +
                $"{result.AnomaliesSaved} new anomal{(result.AnomaliesSaved == 1 ? "y" : "ies")} detected.";
        }
        catch (ArgumentException)
        {
            TempData["Netra.Error"] = "The start date must be on or before the end date.";
        }
        catch (InvalidOperationException)
        {
            TempData["Netra.Error"] = "The assessment could not be recorded.";
        }

        // Redirect with invariant yyyy-MM-dd dates so the filter band round-trips.
        return RedirectToAction(nameof(Index), new
        {
            DateFrom = filter.DateFrom?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            DateTo = filter.DateTo?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            filter.DistrictId,
            filter.CrimeTypeId,
            filter.Severity
        });
    }
}
