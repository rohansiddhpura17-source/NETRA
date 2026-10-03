namespace NETRA.Application.Maps;

/// <summary>One plottable incident derived from a case's incident location.</summary>
public sealed class MapIncidentDto
{
    public required Guid CaseId { get; init; }

    public required string CaseNumber { get; init; }

    public required string Title { get; init; }

    /// <summary>Low / Medium / High / Critical.</summary>
    public required string Severity { get; init; }

    public required string CrimeTypeName { get; init; }

    public required DateTime IncidentDate { get; init; }

    public required string LocationName { get; init; }

    public Guid? DistrictId { get; init; }

    public string? DistrictName { get; init; }

    public required double Latitude { get; init; }

    public required double Longitude { get; init; }

    /// <summary>True when the case is not closed or archived.</summary>
    public required bool IsActive { get; init; }
}

/// <summary>A grid cell containing enough incidents to qualify as a hotspot.</summary>
public sealed class MapHotspotDto
{
    public required double Latitude { get; init; }

    public required double Longitude { get; init; }

    /// <summary>Number of incidents inside the cell.</summary>
    public required int Count { get; init; }
}

/// <summary>District context label placed at the mean of its incidents.</summary>
public sealed class MapDistrictDto
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required double AnchorLatitude { get; init; }

    public required double AnchorLongitude { get; init; }

    public required int IncidentCount { get; init; }
}

/// <summary>Geographic extent of the valid points, padded for display.</summary>
public sealed class MapBoundsDto
{
    public required double MinLatitude { get; init; }

    public required double MinLongitude { get; init; }

    public required double MaxLatitude { get; init; }

    public required double MaxLongitude { get; init; }
}

/// <summary>Everything the Crime Intelligence Map renders.</summary>
public sealed class MapDataDto
{
    public required DateTime DateFrom { get; init; }

    public required DateTime DateTo { get; init; }

    public IReadOnlyList<MapIncidentDto> Incidents { get; init; } = Array.Empty<MapIncidentDto>();

    public IReadOnlyList<MapHotspotDto> Hotspots { get; init; } = Array.Empty<MapHotspotDto>();

    public IReadOnlyList<MapDistrictDto> Districts { get; init; } = Array.Empty<MapDistrictDto>();

    public MapBoundsDto? Bounds { get; init; }

    public int TotalIncidents { get; init; }

    public int ActiveIncidents { get; init; }

    /// <summary>
    /// Case-location rows excluded from the plot because coordinates were missing or
    /// outside valid ranges. Reported to the user, never silently dropped.
    /// </summary>
    public int HiddenRecordCount { get; init; }
}
