using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NETRA.Application.Analytics;
using NETRA.Application.Maps;
using NETRA.Web.Authorization;
using NETRA.Web.Models.ViewModels.Analytics;
using NETRA.Web.Models.ViewModels.Map;

namespace NETRA.Web.Controllers;

/// <summary>
/// Crime Intelligence Map: a server-rendered geographic visualisation of recorded
/// incident coordinates. Not an interactive GIS system — filtering and case
/// quick-view are plain server round-trips.
/// </summary>
[Authorize(Policy = NetraPolicies.CaseRead)]
public class MapController : Controller
{
    private readonly IMapService _map;
    private readonly IAnalyticsService _analytics;

    public MapController(IMapService map, IAnalyticsService analytics)
    {
        _map = map;
        _analytics = analytics;
    }

    [HttpGet("/map")]
    public async Task<IActionResult> Index(
        AnalyticsFilterViewModel filter,
        Guid? caseId,
        CancellationToken cancellationToken)
    {
        MapDataDto data;
        var criteria = filter.ToCriteria();

        try
        {
            data = await _map.GetMapAsync(criteria, cancellationToken);
        }
        catch (ArgumentException)
        {
            ModelState.AddModelError(nameof(filter.DateTo),
                "The start date must be on or before the end date.");
            data = await _map.GetMapAsync(new AnalyticsFilterCriteria(), cancellationToken);
        }

        var lookups = await _analytics.GetLookupsAsync(cancellationToken);
        return View(MapViewModel.Create(data, filter, lookups, caseId));
    }
}
