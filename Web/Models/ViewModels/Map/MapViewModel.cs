using System.Globalization;
using NETRA.Application.Analytics;
using NETRA.Application.Maps;
using NETRA.Web.Models.Navigation;
using NETRA.Web.Models.Ui;
using NETRA.Web.Models.ViewModels.Analytics;
using NETRA.Web.Services.Maps;

namespace NETRA.Web.Models.ViewModels.Map;

/// <summary>
/// Presentation model for the Crime Intelligence Map. Coordinates, hotspot
/// clustering and counts come from <see cref="MapDataDto"/>; this class only
/// formats them for the view and builds the renderer input.
/// </summary>
public sealed class MapViewModel
{
    public required MapDataDto Data { get; init; }
    public required AnalyticsFilterViewModel Filter { get; init; }
    public required AnalyticsLookupDto Lookups { get; init; }
    public Guid? SelectedCaseId { get; init; }

    public SvgMapModel? Map { get; init; }
    public IReadOnlyList<StatCardModel> SummaryCards { get; init; } = Array.Empty<StatCardModel>();
    public IReadOnlyList<IncidentRowItem> Incidents { get; init; } = Array.Empty<IncidentRowItem>();
    public IncidentRowItem? SelectedIncident { get; init; }

    public string WindowLabel => $"{Data.DateFrom:dd MMM yyyy} – {Data.DateTo:dd MMM yyyy}";

    public bool HasPlottableIncidents => Data.TotalIncidents > 0;

    public static MapViewModel Create(
        MapDataDto data,
        AnalyticsFilterViewModel filter,
        AnalyticsLookupDto lookups,
        Guid? selectedCaseId)
    {
        var incidents = data.Incidents.Select(p => new IncidentRowItem
        {
            CaseId = p.CaseId,
            CaseNumber = p.CaseNumber,
            Title = p.Title,
            SeverityLabel = p.Severity,
            SeverityTone = SeverityTone(p.Severity),
            CrimeTypeName = p.CrimeTypeName,
            IncidentDateLabel = p.IncidentDate.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture),
            LocationName = p.LocationName,
            DistrictName = p.DistrictName ?? "—",
            IsActive = p.IsActive
        }).ToList();

        var selected = incidents.FirstOrDefault(i => i.CaseId == selectedCaseId);

        SvgMapModel? map = null;
        if (data.Bounds is { } bounds && data.Incidents.Count > 0)
        {
            map = new SvgMapModel
            {
                Title = "Crime incident map",
                Description =
                    $"Server-rendered geographic visualisation of {data.TotalIncidents} plotted incident" +
                    $"{(data.TotalIncidents == 1 ? "" : "s")} between {data.DateFrom:dd MMM yyyy} and " +
                    $"{data.DateTo:dd MMM yyyy}, including {data.Hotspots.Count} hotspot " +
                    $"cluster{(data.Hotspots.Count == 1 ? "" : "s")}. " +
                    "Marker colour indicates case severity. This is a schematic projection of recorded " +
                    "coordinates, not an interactive map.",
                MinLatitude = bounds.MinLatitude,
                MaxLatitude = bounds.MaxLatitude,
                MinLongitude = bounds.MinLongitude,
                MaxLongitude = bounds.MaxLongitude,
                Points = data.Incidents.Select(p => new SvgMapPoint
                {
                    Latitude = p.Latitude,
                    Longitude = p.Longitude,
                    SeverityClass = p.Severity.ToLowerInvariant(),
                    Label = $"{p.CaseNumber}: {p.Title} ({p.Severity}) at {p.LocationName}"
                }).ToList(),
                Hotspots = data.Hotspots.Select(h => new SvgMapHotspot
                {
                    Latitude = h.Latitude,
                    Longitude = h.Longitude,
                    Count = h.Count
                }).ToList(),
                Districts = data.Districts.Select(d => new SvgMapDistrictLabel
                {
                    Latitude = d.AnchorLatitude,
                    Longitude = d.AnchorLongitude,
                    Name = d.Name
                }).ToList()
            };
        }

        return new MapViewModel
        {
            Data = data,
            Filter = filter,
            Lookups = lookups,
            SelectedCaseId = selectedCaseId,
            Map = map,
            SelectedIncident = selected,
            Incidents = incidents,
            SummaryCards =
            [
                new StatCardModel
                {
                    Label = "Active Incidents",
                    Value = data.ActiveIncidents.ToString("N0", CultureInfo.InvariantCulture),
                    Meta = WindowMeta(data),
                    Tone = NetraTone.Primary,
                    Icon = NetraIcon.Alert
                },
                new StatCardModel
                {
                    Label = "Plotted Incidents",
                    Value = data.TotalIncidents.ToString("N0", CultureInfo.InvariantCulture),
                    Meta = $"{data.Districts.Count} district{(data.Districts.Count == 1 ? "" : "s")} in view",
                    Tone = NetraTone.Info,
                    Icon = NetraIcon.Map
                },
                new StatCardModel
                {
                    Label = "Hotspots",
                    Value = data.Hotspots.Count.ToString(CultureInfo.InvariantCulture),
                    Meta = "Grid clusters of 3+ incidents",
                    Tone = data.Hotspots.Count > 0 ? NetraTone.Warning : NetraTone.Success,
                    Icon = NetraIcon.Analytics
                },
                new StatCardModel
                {
                    Label = "Records Without Coordinates",
                    Value = data.HiddenRecordCount.ToString(CultureInfo.InvariantCulture),
                    Meta = data.HiddenRecordCount > 0
                        ? "Excluded from the plot, listed for review"
                        : "All filtered records have usable coordinates",
                    Tone = data.HiddenRecordCount > 0 ? NetraTone.Warning : NetraTone.Neutral,
                    Icon = NetraIcon.Info
                }
            ]
        };
    }

    private static string WindowMeta(MapDataDto data) =>
        $"{data.DateFrom:dd MMM} – {data.DateTo:dd MMM yyyy}";

    private static NetraTone SeverityTone(string severity) => severity switch
    {
        "Critical" => NetraTone.Danger,
        "High" => NetraTone.Warning,
        "Medium" => NetraTone.Info,
        _ => NetraTone.Success
    };
}

/// <summary>Case quick-view row rendered beside the map (server-side, no JS popups).</summary>
public sealed class IncidentRowItem
{
    public required Guid CaseId { get; init; }
    public required string CaseNumber { get; init; }
    public required string Title { get; init; }
    public required string SeverityLabel { get; init; }
    public required NetraTone SeverityTone { get; init; }
    public required string CrimeTypeName { get; init; }
    public required string IncidentDateLabel { get; init; }
    public required string LocationName { get; init; }
    public required string DistrictName { get; init; }
    public required bool IsActive { get; init; }
}
