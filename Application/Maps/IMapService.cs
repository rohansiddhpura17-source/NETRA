using NETRA.Application.Analytics;

namespace NETRA.Application.Maps;

/// <summary>
/// Aggregates geocoded incident data for the Crime Intelligence Map. The output is
/// a server-rendered geographic visualisation, not a live interactive GIS system.
/// </summary>
public interface IMapService
{
    /// <summary>
    /// Filtered, projected incident points with hotspot clustering and district
    /// context. The default window is the trailing 30 days.
    /// </summary>
    Task<MapDataDto> GetMapAsync(
        AnalyticsFilterCriteria criteria,
        CancellationToken cancellationToken = default);
}
