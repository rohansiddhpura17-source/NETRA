using Microsoft.EntityFrameworkCore;
using NETRA.Application.Analytics;
using NETRA.Application.Maps;
using NETRA.Domain.Cases;
using NETRA.Domain.Enums;
using NETRA.Domain.Geography;
using NETRA.Domain.Security;
using NETRA.Infrastructure.Data;
using NETRA.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace NETRA.Tests.Application;

/// <summary>
/// Service-level tests for <see cref="MapService"/>: coordinate validation
/// (bad records are reported, never plotted), shared filtering, deterministic
/// grid clustering, bounds padding and honest empty states.
/// </summary>
public sealed class MapServiceTests : IDisposable
{
    // Fixed clock: default window 2026-05-17..2026-06-15.
    private static readonly DateTime FixedToday = new(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime WindowDate = new(2026, 6, 5, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime OutsideDate = new(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);

    private readonly NetraDbContext _context;
    private readonly MapService _service;
    private readonly Guid _actorId = Guid.NewGuid();
    private readonly Guid _theftId;
    private readonly Guid _centralId;
    private readonly Guid _northId;

    public MapServiceTests()
    {
        _context = new NetraDbContext(
            new DbContextOptionsBuilder<NetraDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        _context.Users.Add(new ApplicationUser
        {
            Id = _actorId,
            UserName = "map.seed@netra.local",
            Email = "map.seed@netra.local",
            FirstName = "Map",
            LastName = "Seed",
            EmailConfirmed = true,
            IsActive = true
        });

        var theft = new CrimeType { Name = "Theft", Code = "THF", DisplayOrder = 1, IsActive = true };
        var central = new District { Name = "Central", Code = "CD", IsActive = true };
        var north = new District { Name = "North", Code = "ND", IsActive = true };
        _context.CrimeTypes.Add(theft);
        _context.Districts.AddRange(central, north);
        _context.SaveChanges();

        _theftId = theft.Id;
        _centralId = central.Id;
        _northId = north.Id;

        _service = new MapService(_context, new FixedTimeProvider(FixedToday));
    }

    public void Dispose() => _context.Dispose();

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private CrimeCase AddCase(
        string caseNumber,
        double? latitude,
        double? longitude,
        Guid? districtId = null,
        CaseSeverity severity = CaseSeverity.Medium,
        CaseStatus status = CaseStatus.Open,
        DateTime? incidentDate = null)
    {
        var date = incidentDate ?? WindowDate;
        var crimeCase = new CrimeCase
        {
            CaseNumber = caseNumber,
            Title = $"Case {caseNumber}",
            CrimeTypeId = _theftId,
            Status = status,
            Severity = severity,
            IncidentDate = date,
            CreatedById = _actorId,
            CreatedAt = date
        };
        _context.CrimeCases.Add(crimeCase);

        var location = new Location
        {
            Name = $"Scene {caseNumber}",
            DistrictId = districtId,
            Latitude = latitude,
            Longitude = longitude,
            CreatedAt = date
        };
        _context.Locations.Add(location);
        _context.CaseLocations.Add(new CaseLocation
        {
            CrimeCaseId = crimeCase.Id,
            LocationId = location.Id,
            LocationRole = CaseService.IncidentLocationRole,
            AssociatedAt = date
        });

        _context.SaveChanges();
        return crimeCase;
    }

    private static AnalyticsFilterCriteria NoFilter() => new();

    // ------------------------------------------------------------ window + filters

    [Fact]
    public async Task GetMapAsync_DefaultWindow_IsTrailingThirtyDays()
    {
        var data = await _service.GetMapAsync(NoFilter());

        data.DateFrom.Should().Be(new DateTime(2026, 5, 17));
        data.DateTo.Should().Be(new DateTime(2026, 6, 15));
    }

    [Fact]
    public async Task GetMapAsync_WindowExcludesCasesOutsideDateRange()
    {
        AddCase("CC-1", 51.5072, -0.1276, _centralId, incidentDate: WindowDate);
        AddCase("CC-2", 51.5073, -0.1277, _centralId, incidentDate: OutsideDate);

        var data = await _service.GetMapAsync(NoFilter());

        data.Incidents.Should().ContainSingle(i => i.CaseNumber == "CC-1");
    }

    [Fact]
    public async Task GetMapAsync_InvertedDates_IsRejected()
    {
        var act = () => _service.GetMapAsync(new AnalyticsFilterCriteria
        {
            DateFrom = new DateTime(2026, 6, 10),
            DateTo = new DateTime(2026, 6, 1)
        });

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*on or before*");
    }

    [Fact]
    public async Task GetMapAsync_DistrictSeverityAndCrimeTypeFilters_AppliedServerSide()
    {
        AddCase("CC-1", 51.5072, -0.1276, _centralId, CaseSeverity.Critical);
        AddCase("CC-2", 51.5073, -0.1277, _centralId, CaseSeverity.Low);
        AddCase("CC-3", 51.5231, -0.1425, _northId, CaseSeverity.Critical);

        var districtOnly = await _service.GetMapAsync(
            new AnalyticsFilterCriteria { DistrictId = _centralId });
        districtOnly.Incidents.Select(i => i.CaseNumber)
            .Should().BeEquivalentTo(["CC-1", "CC-2"]);
        districtOnly.Incidents.Should().OnlyContain(i => i.DistrictName == "Central");

        var severityOnly = await _service.GetMapAsync(
            new AnalyticsFilterCriteria { Severity = "Critical" });
        severityOnly.Incidents.Select(i => i.CaseNumber).Should().BeEquivalentTo(["CC-1", "CC-3"]);

        var typeOnly = await _service.GetMapAsync(
            new AnalyticsFilterCriteria { CrimeTypeId = _theftId });
        typeOnly.TotalIncidents.Should().Be(3);

        var wrongType = await _service.GetMapAsync(
            new AnalyticsFilterCriteria { CrimeTypeId = Guid.NewGuid() });
        wrongType.Incidents.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMapAsync_NonIncidentLocationRole_IsIgnored()
    {
        var crimeCase = AddCase("CC-1", 51.5072, -0.1276, _centralId);
        var link = await _context.CaseLocations.SingleAsync(cl => cl.CrimeCaseId == crimeCase.Id);
        link.LocationRole = "Station";
        await _context.SaveChangesAsync();

        var data = await _service.GetMapAsync(NoFilter());

        data.Incidents.Should().BeEmpty();
        data.HiddenRecordCount.Should().Be(0);
    }

    // ------------------------------------------------------------ coordinate validation

    [Theory]
    [InlineData(null, null, "missing coordinates")]
    [InlineData(0.0, 0.0, "the 0,0 placeholder")]
    [InlineData(95.0, -0.1, "latitude outside -90..90")]
    [InlineData(51.5, 200.0, "longitude outside -180..180")]
    public async Task GetMapAsync_InvalidCoordinates_AreReportedNotPlotted(
        double? latitude,
        double? longitude,
        string reason)
    {
        AddCase("CC-bad", latitude, longitude, _centralId);
        AddCase("CC-good", 51.5072, -0.1276, _centralId);

        var data = await _service.GetMapAsync(NoFilter());

        data.Incidents.Should().ContainSingle(i => i.CaseNumber == "CC-good", reason);
        data.HiddenRecordCount.Should().Be(1);
        // The single valid point keeps the bounds around it, padded by the minimum.
        data.Bounds!.MinLatitude.Should().BeApproximately(51.4972, 1e-6);
    }

    [Fact]
    public async Task GetMapAsync_BadRecordsDoNotBreakAGoodMap()
    {
        AddCase("CC-1", 51.5072, -0.1276, _centralId);
        AddCase("CC-2", null, null, _centralId);
        AddCase("CC-3", 0, 0, _northId);
        AddCase("CC-4", 51.5231, -0.1425, _northId);

        var data = await _service.GetMapAsync(NoFilter());

        data.TotalIncidents.Should().Be(2);
        data.HiddenRecordCount.Should().Be(2);
        data.Incidents.Should().OnlyContain(p =>
            p.Latitude >= -90 && p.Latitude <= 90 &&
            p.Longitude >= -180 && p.Longitude <= 180);
    }

    // ------------------------------------------------------------ aggregation

    [Fact]
    public async Task GetMapAsync_ActiveIncidents_CountNonClosedCases()
    {
        AddCase("CC-1", 51.5072, -0.1276, _centralId, status: CaseStatus.Open);
        AddCase("CC-2", 51.5073, -0.1277, _centralId, status: CaseStatus.Closed);
        AddCase("CC-3", 51.5074, -0.1278, _centralId, status: CaseStatus.Archived);
        AddCase("CC-4", 51.5075, -0.1279, _centralId, status: CaseStatus.UnderInvestigation);

        var data = await _service.GetMapAsync(NoFilter());

        data.TotalIncidents.Should().Be(4);
        data.ActiveIncidents.Should().Be(2);
    }

    [Fact]
    public async Task GetMapAsync_DenseCell_BecomesOneHotspot_SparseCellsDoNot()
    {
        // Four incidents within ~100 m of each other plus two in another district.
        AddCase("CC-h1", 51.5072, -0.1276, _centralId);
        AddCase("CC-h2", 51.5073, -0.1275, _centralId);
        AddCase("CC-h3", 51.50725, -0.12765, _centralId);
        AddCase("CC-h4", 51.50728, -0.12758, _centralId);
        AddCase("CC-n1", 51.5231, -0.1425, _northId);
        AddCase("CC-n2", 51.5235, -0.1430, _northId);

        var data = await _service.GetMapAsync(NoFilter());

        data.Hotspots.Should().ContainSingle()
            .Which.Count.Should().Be(4);
        data.Hotspots.Single().Latitude.Should().BeInRange(51.50, 51.53);
    }

    [Fact]
    public async Task GetMapAsync_BelowThreshold_NoHotspots()
    {
        AddCase("CC-1", 51.5072, -0.1276, _centralId);
        AddCase("CC-2", 51.5231, -0.1425, _northId);

        var data = await _service.GetMapAsync(NoFilter());

        data.Hotspots.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMapAsync_Bounds_PadThePointExtentForDisplay()
    {
        AddCase("CC-1", 51.50, -0.12, _centralId);
        AddCase("CC-2", 52.50, -0.02, _centralId);

        var bounds = (await _service.GetMapAsync(NoFilter())).Bounds!;

        // 1.0 degree latitude span and 0.1 degree longitude span pad by 10% each side.
        bounds.MinLatitude.Should().BeApproximately(51.40, 1e-9);
        bounds.MaxLatitude.Should().BeApproximately(52.60, 1e-9);
        bounds.MinLongitude.Should().BeApproximately(-0.13, 1e-9);
        bounds.MaxLongitude.Should().BeApproximately(-0.01, 1e-9);
    }

    [Fact]
    public async Task GetMapAsync_SinglePoint_BoundsUseMinimumPadding()
    {
        AddCase("CC-1", 51.5072, -0.1276, _centralId);

        var bounds = (await _service.GetMapAsync(NoFilter())).Bounds!;

        bounds.MinLatitude.Should().BeApproximately(51.4972, 1e-9);
        bounds.MaxLatitude.Should().BeApproximately(51.5172, 1e-9);
    }

    [Fact]
    public async Task GetMapAsync_DistrictAnchors_AreTheMeanOfTheirPoints()
    {
        AddCase("CC-1", 51.5072, -0.1276, _centralId);
        AddCase("CC-2", 51.5172, -0.1376, _centralId);
        AddCase("CC-3", 51.5231, -0.1425, _northId);
        AddCase("CC-4", 51.5, -0.12, null); // plotted but no district context

        var data = await _service.GetMapAsync(NoFilter());

        data.Districts.Should().HaveCount(2);
        var central = data.Districts.Single(d => d.Name == "Central");
        central.IncidentCount.Should().Be(2);
        central.AnchorLatitude.Should().BeApproximately(51.5122, 1e-6);
        central.AnchorLongitude.Should().BeApproximately(-0.1326, 1e-6);
        data.Incidents.Should().ContainSingle(i => i.DistrictId == null);
    }

    [Fact]
    public async Task GetMapAsync_MostRecentFirst_AndDeterministicOrdering()
    {
        AddCase("CC-old", 51.50, -0.12, _centralId,
            incidentDate: new DateTime(2026, 5, 18, 8, 0, 0, DateTimeKind.Utc));
        AddCase("CC-new", 51.51, -0.13, _centralId,
            incidentDate: new DateTime(2026, 6, 14, 8, 0, 0, DateTimeKind.Utc));
        AddCase("CC-mid", 51.52, -0.14, _centralId,
            incidentDate: new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc));

        var first = await _service.GetMapAsync(NoFilter());
        var second = await _service.GetMapAsync(NoFilter());

        first.Incidents.Select(i => i.CaseNumber)
            .Should().ContainInOrder("CC-new", "CC-mid", "CC-old");
        second.Incidents.Select(i => i.CaseNumber)
            .Should().Equal(first.Incidents.Select(i => i.CaseNumber));
        second.Bounds!.MaxLatitude.Should().Be(first.Bounds!.MaxLatitude);
    }

    [Fact]
    public async Task GetMapAsync_NoCases_HonestEmptyStateWithoutBounds()
    {
        var data = await _service.GetMapAsync(NoFilter());

        data.Incidents.Should().BeEmpty();
        data.Hotspots.Should().BeEmpty();
        data.Districts.Should().BeEmpty();
        data.Bounds.Should().BeNull();
        data.TotalIncidents.Should().Be(0);
        data.ActiveIncidents.Should().Be(0);
        data.HiddenRecordCount.Should().Be(0);
    }
}
