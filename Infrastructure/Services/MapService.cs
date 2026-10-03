using Microsoft.EntityFrameworkCore;
using NETRA.Application.Analytics;
using NETRA.Application.Maps;
using NETRA.Domain.Cases;
using NETRA.Domain.Enums;
using NETRA.Infrastructure.Data;

namespace NETRA.Infrastructure.Services;

/// <summary>
/// <h2>Server-side geographic visualisation</h2>
/// <para>
/// Projects filtered cases to their "Incident" case-location coordinates. Rows
/// without usable coordinates (missing, out of range, or the 0,0 placeholder) are
/// excluded from the plot and reported as <see cref="MapDataDto.HiddenRecordCount"/>
/// rather than silently dropped, so one bad record can never crash or distort the map.
/// </para>
/// <para>
/// Hotspots are a deterministic grid cluster: the padded bounding box of the valid
/// points is divided into <see cref="GridCells"/> × <see cref="GridCells"/> cells and
/// any cell holding at least <see cref="HotspotThreshold"/> incidents becomes a
/// hotspot at the cell centre. District labels anchor at the mean of their points.
/// Everything is computed from projections with <c>AsNoTracking</c>; no case
/// entities are loaded.
/// </para>
/// </summary>
public sealed class MapService : IMapService
{
    public const int DefaultWindowDays = 30;

    /// <summary>Grid resolution for hotspot clustering.</summary>
    public const int GridCells = 8;

    /// <summary>Incidents inside one grid cell before it is reported as a hotspot.</summary>
    public const int HotspotThreshold = 3;

    /// <summary>Upper bound on plotted points to keep the SVG readable and the query cheap.</summary>
    public const int MaxPlottedIncidents = 400;

    private readonly NetraDbContext _context;
    private readonly TimeProvider _timeProvider;

    public MapService(NetraDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<MapDataDto> GetMapAsync(
        AnalyticsFilterCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        var today = _timeProvider.GetUtcNow().UtcDateTime.Date;
        var dateTo = (criteria.DateTo ?? today).Date;
        var dateFrom = (criteria.DateFrom ?? dateTo.AddDays(-(DefaultWindowDays - 1))).Date;

        if (dateFrom > dateTo)
        {
            throw new ArgumentException("Date From must be on or before Date To.", nameof(criteria));
        }

        // Incident-location rows for the window. The projection is an anonymous shape
        // (record constructors inside a translatable projection are not portable across
        // providers); DTOs are built client-side from the bounded, distinct row set.
        var rows = CaseQueryFilters.Apply(
                _context.CrimeCases.AsNoTracking(), criteria, dateFrom, dateTo)
            .SelectMany(c => c.CaseLocations
                .Where(cl => cl.LocationRole == CaseService.IncidentLocationRole && cl.Location != null)
                .Select(cl => new
                {
                    CaseId = c.Id,
                    CaseNumber = c.CaseNumber,
                    Title = c.Title,
                    c.Severity,
                    c.IncidentDate,
                    LocationName = cl.Location!.Name,
                    Latitude = cl.Location.Latitude,
                    Longitude = cl.Location.Longitude,
                    DistrictId = cl.Location.DistrictId,
                    DistrictName = cl.Location.District != null ? cl.Location.District.Name : null,
                    CrimeTypeName = c.CrimeType.Name,
                    IsActive = c.Status != CaseStatus.Closed && c.Status != CaseStatus.Archived
                }))
            .Distinct();

        var hiddenCount = await rows
            .Where(x => x.Latitude == null || x.Longitude == null
                        || x.Latitude < -90 || x.Latitude > 90
                        || x.Longitude < -180 || x.Longitude > 180
                        || (x.Latitude == 0 && x.Longitude == 0))
            .Select(x => x.CaseId)
            .Distinct()
            .CountAsync(cancellationToken);

        var plotted = (await rows
            .Where(x => x.Latitude != null && x.Longitude != null
                        && x.Latitude >= -90 && x.Latitude <= 90
                        && x.Longitude >= -180 && x.Longitude <= 180
                        && !(x.Latitude == 0 && x.Longitude == 0))
            .OrderByDescending(x => x.IncidentDate)
            .ThenBy(x => x.CaseNumber)
            .Take(MaxPlottedIncidents)
            .ToListAsync(cancellationToken))
            .Select(x => new MapIncidentDto
            {
                CaseId = x.CaseId,
                CaseNumber = x.CaseNumber,
                Title = x.Title,
                Severity = x.Severity.ToString(),
                CrimeTypeName = x.CrimeTypeName,
                IncidentDate = x.IncidentDate,
                LocationName = x.LocationName,
                DistrictId = x.DistrictId,
                DistrictName = x.DistrictName,
                Latitude = x.Latitude!.Value,
                Longitude = x.Longitude!.Value,
                IsActive = x.IsActive
            })
            .ToList();

        var bounds = ComputeBounds(plotted);
        var hotspots = bounds is null
            ? []
            : ClusterHotspots(plotted, bounds);

        var districts = plotted
            .Where(p => p.DistrictId is not null)
            .GroupBy(p => (Id: p.DistrictId!.Value, Name: p.DistrictName!))
            .OrderBy(g => g.Key.Name)
            .Select(g => new MapDistrictDto
            {
                Id = g.Key.Id,
                Name = g.Key.Name,
                AnchorLatitude = Math.Round(g.Average(p => p.Latitude), 6),
                AnchorLongitude = Math.Round(g.Average(p => p.Longitude), 6),
                IncidentCount = g.Count()
            })
            .ToList();

        return new MapDataDto
        {
            DateFrom = dateFrom,
            DateTo = dateTo,
            Incidents = plotted,
            Hotspots = hotspots,
            Districts = districts,
            Bounds = bounds,
            TotalIncidents = plotted.Count,
            ActiveIncidents = plotted.Count(p => p.IsActive),
            HiddenRecordCount = hiddenCount
        };
    }

    // ------------------------------------------------------------------ helpers

    private static MapBoundsDto? ComputeBounds(IReadOnlyList<MapIncidentDto> points)
    {
        if (points.Count == 0)
        {
            return null;
        }

        var minLat = points.Min(p => p.Latitude);
        var maxLat = points.Max(p => p.Latitude);
        var minLon = points.Min(p => p.Longitude);
        var maxLon = points.Max(p => p.Longitude);

        // Pad by 10% of the span (or a fixed degree step for near-degenerate sets)
        // so markers never sit on the frame edge.
        var padLat = Math.Max((maxLat - minLat) * 0.10, 0.01);
        var padLon = Math.Max((maxLon - minLon) * 0.10, 0.01);

        return new MapBoundsDto
        {
            MinLatitude = minLat - padLat,
            MaxLatitude = maxLat + padLat,
            MinLongitude = minLon - padLon,
            MaxLongitude = maxLon + padLon
        };
    }

    private static IReadOnlyList<MapHotspotDto> ClusterHotspots(
        IReadOnlyList<MapIncidentDto> points,
        MapBoundsDto bounds)
    {
        var latSpan = bounds.MaxLatitude - bounds.MinLatitude;
        var lonSpan = bounds.MaxLongitude - bounds.MinLongitude;

        var cells = new Dictionary<(int Row, int Col), List<MapIncidentDto>>();

        foreach (var point in points)
        {
            var row = Math.Clamp(
                (int)((point.Latitude - bounds.MinLatitude) / latSpan * GridCells), 0, GridCells - 1);
            var col = Math.Clamp(
                (int)((point.Longitude - bounds.MinLongitude) / lonSpan * GridCells), 0, GridCells - 1);

            if (!cells.TryGetValue((row, col), out var list))
            {
                list = [];
                cells[(row, col)] = list;
            }

            list.Add(point);
        }

        return cells
            .Where(cell => cell.Value.Count >= HotspotThreshold)
            .Select(cell => new MapHotspotDto
            {
                Latitude = Math.Round(
                    bounds.MinLatitude + ((cell.Key.Row + 0.5) / GridCells) * latSpan, 6),
                Longitude = Math.Round(
                    bounds.MinLongitude + ((cell.Key.Col + 0.5) / GridCells) * lonSpan, 6),
                Count = cell.Value.Count
            })
            .OrderByDescending(h => h.Count)
            .ThenBy(h => h.Latitude)
            .ThenBy(h => h.Longitude)
            .ToList();
    }
}
