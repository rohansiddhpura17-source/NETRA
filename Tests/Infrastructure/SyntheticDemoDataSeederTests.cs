using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NETRA.Application.Abstractions;
using NETRA.Application.Analytics;
using NETRA.Application.Dashboard;
using NETRA.Domain.Alerts;
using NETRA.Domain.Cases;
using NETRA.Domain.Enums;
using NETRA.Domain.Geography;
using NETRA.Domain.Intelligence;
using NETRA.Domain.Security;
using NETRA.Infrastructure.Data;
using NETRA.Infrastructure.Identity;
using NETRA.Infrastructure.Services;
using Xunit;

namespace NETRA.Tests.Infrastructure;

public sealed class SyntheticDemoDataSeederTests : IDisposable
{
    private readonly NetraDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly CaseService _caseService;
    private readonly Guid _adminId = Guid.NewGuid();

    public SyntheticDemoDataSeederTests()
    {
        var options = new DbContextOptionsBuilder<NetraDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new NetraDbContext(options);

        _userManager = new UserManager<ApplicationUser>(
            new InMemoryUserStore(_context),
            null!, null!, null!, null!, null!, null!, null!, null!);

        var audit = new AuditService(
            _context,
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            new StubActorAccessor(_adminId));

        IFileStorageService storage = new StubFileStorageService();
        _caseService = new CaseService(_context, audit, storage);

        SeedBaselineLookups();
    }

    public void Dispose()
    {
        _context.Dispose();
        Environment.SetEnvironmentVariable(SyntheticDemoDataSeeder.SeedSyntheticDemoCasesEnvVar, null);
    }

    private void SeedBaselineLookups()
    {
        var admin = new ApplicationUser
        {
            Id = _adminId,
            UserName = "admin@netra.local",
            Email = "admin@netra.local",
            FirstName = "System",
            LastName = "Admin",
            EmailConfirmed = true,
            IsActive = true
        };
        _context.Users.Add(admin);

        _context.Districts.AddRange(
            new District { Id = Guid.NewGuid(), Name = "Central District", Code = "CD", IsActive = true, CreatedAt = DateTime.UtcNow },
            new District { Id = Guid.NewGuid(), Name = "North District", Code = "ND", IsActive = true, CreatedAt = DateTime.UtcNow },
            new District { Id = Guid.NewGuid(), Name = "South District", Code = "SD", IsActive = true, CreatedAt = DateTime.UtcNow },
            new District { Id = Guid.NewGuid(), Name = "East District", Code = "ED", IsActive = true, CreatedAt = DateTime.UtcNow },
            new District { Id = Guid.NewGuid(), Name = "West District", Code = "WD", IsActive = true, CreatedAt = DateTime.UtcNow });

        _context.CrimeTypes.AddRange(
            new CrimeType { Id = Guid.NewGuid(), Name = "Fraud", Code = "FRD", IsActive = true, CreatedAt = DateTime.UtcNow },
            new CrimeType { Id = Guid.NewGuid(), Name = "Theft", Code = "THF", IsActive = true, CreatedAt = DateTime.UtcNow },
            new CrimeType { Id = Guid.NewGuid(), Name = "Burglary", Code = "BUR", IsActive = true, CreatedAt = DateTime.UtcNow },
            new CrimeType { Id = Guid.NewGuid(), Name = "Assault", Code = "ASL", IsActive = true, CreatedAt = DateTime.UtcNow },
            new CrimeType { Id = Guid.NewGuid(), Name = "Robbery", Code = "ROB", IsActive = true, CreatedAt = DateTime.UtcNow },
            new CrimeType { Id = Guid.NewGuid(), Name = "Homicide", Code = "HOM", IsActive = true, CreatedAt = DateTime.UtcNow });

        _context.SaveChanges();
    }

    private SyntheticDemoDataSeeder CreateSeeder(
        bool isDevelopment,
        bool? configFlag = null,
        string? envVar = null)
    {
        if (envVar is not null)
        {
            Environment.SetEnvironmentVariable(SyntheticDemoDataSeeder.SeedSyntheticDemoCasesEnvVar, envVar);
        }

        var configBuilder = new ConfigurationBuilder();
        if (configFlag.HasValue)
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SyntheticDemoDataSeeder.SeedSyntheticDemoCasesConfigKey] = configFlag.Value ? "true" : "false"
            });
        }
        var configuration = configBuilder.Build();

        var environment = new FakeHostEnvironment
        {
            EnvironmentName = isDevelopment ? Environments.Development : Environments.Production
        };

        return new SyntheticDemoDataSeeder(
            _context,
            _caseService,
            _userManager,
            NullLogger<SyntheticDemoDataSeeder>.Instance,
            configuration,
            environment);
    }

    [Fact]
    public void DemonstrationSeeding_IsDisabledByDefault()
    {
        var seeder = CreateSeeder(isDevelopment: true, configFlag: null);
        seeder.IsEnabled().Should().BeFalse();
    }

    [Fact]
    public void DemonstrationSeeding_EnablingSeedingOnlyInDevelopment()
    {
        var prodSeeder = CreateSeeder(isDevelopment: false, configFlag: true);
        prodSeeder.IsEnabled().Should().BeFalse("Production environment must never allow demo seeding");

        var devSeeder = CreateSeeder(isDevelopment: true, configFlag: true);
        devSeeder.IsEnabled().Should().BeTrue();

        var envSeeder = CreateSeeder(isDevelopment: true, configFlag: null, envVar: "true");
        envSeeder.IsEnabled().Should().BeTrue();
    }

    [Fact]
    public async Task DemonstrationSeeding_Seeds10CasesAndAlertsAndRiskAssessments()
    {
        var seeder = CreateSeeder(isDevelopment: true, configFlag: true);

        var created = await seeder.SeedAsync();
        created.Should().Be(10);

        var cases = await _context.CrimeCases.OrderBy(c => c.CaseNumber).ToListAsync();
        cases.Should().HaveCount(10);
        cases.Select(c => c.CaseNumber).Should().Equal(
            "CC-2026-0004", "CC-2026-0005", "CC-2026-0006", "CC-2026-0007", "CC-2026-0008",
            "CC-2026-0009", "CC-2026-0010", "CC-2026-0011", "CC-2026-0012", "CC-2026-0013");

        cases.Should().AllSatisfy(c =>
        {
            c.Title.Should().StartWith("[DEMO — SYNTHETIC DATA]");
            c.Description.Should().StartWith("[DEMO — SYNTHETIC DATA]");
        });

        // 4 Alerts linked to cases
        var alerts = await _context.Alerts.ToListAsync();
        alerts.Should().HaveCount(4);
        alerts.Should().AllSatisfy(a =>
        {
            a.AlertKey.Should().StartWith("DEMO-ALERT-CC-2026-");
            a.Title.Should().StartWith("[DEMO — SYNTHETIC DATA]");
            a.CrimeCaseId.Should().NotBeNull();
        });

        // 4 RiskAssessments with RiskLevel >= 3 linked to cases
        var assessments = await _context.RiskAssessments.ToListAsync();
        assessments.Should().HaveCount(4);
        assessments.Should().AllSatisfy(r =>
        {
            r.Title.Should().StartWith("[DEMO — SYNTHETIC DATA]");
            r.RiskLevel.Should().BeGreaterThanOrEqualTo(3);
            r.CrimeCaseId.Should().NotBeNull();
            r.DistrictId.Should().NotBeNull();
        });
    }

    [Fact]
    public async Task DemonstrationSeeding_IsIdempotent()
    {
        var seeder = CreateSeeder(isDevelopment: true, configFlag: true);

        var firstRun = await seeder.SeedAsync();
        firstRun.Should().Be(10);

        var secondRun = await seeder.SeedAsync();
        secondRun.Should().Be(0, "Repeated runs must not duplicate records");

        (await _context.CrimeCases.CountAsync()).Should().Be(10);
        (await _context.Alerts.CountAsync()).Should().Be(4);
        (await _context.RiskAssessments.CountAsync()).Should().Be(4);
    }

    [Fact]
    public async Task DemonstrationSeeding_DashboardReflectsAccurateTotals()
    {
        var seeder = CreateSeeder(isDevelopment: true, configFlag: true);
        await seeder.SeedAsync();

        var timeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
        var analytics = new AnalyticsService(_context, timeProvider);
        var dashboardService = new DashboardService(_context, analytics, timeProvider);

        var dashboard = await dashboardService.GetDashboardAsync();

        dashboard.TotalCases.Should().Be(10);
        // 2 closed out of 10 -> 8 active cases
        dashboard.ActiveCases.Should().Be(8);
        dashboard.ActiveAlerts.Should().Be(4);
        dashboard.HighRiskAreaCount.Should().Be(4);
        dashboard.HighRiskAreas.Should().HaveCount(4);
        dashboard.RecentAlerts.Should().HaveCount(4);
        dashboard.CrimeTrend.Should().NotBeEmpty();
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _fixedTime;
        public FixedTimeProvider(DateTimeOffset fixedTime) => _fixedTime = fixedTime;
        public override DateTimeOffset GetUtcNow() => _fixedTime;
    }

    private sealed class StubActorAccessor : IAuditActorAccessor
    {
        private readonly Guid _id;
        public StubActorAccessor(Guid id) => _id = id;
        public Guid GetUserId() => _id;
        public Guid GetUserId(System.Security.Claims.ClaimsPrincipal? principal) => _id;
    }

    private sealed class StubFileStorageService : IFileStorageService
    {
        public IReadOnlyCollection<string> AllowedExtensions => [".pdf", ".png"];
        public long MaxFileSizeBytes => 1024 * 1024;
        public FileValidationResult Validate(string fileName, string contentType, long length) =>
            FileValidationResult.Valid(".png");
        public Task<StoredFileResult> SaveAsync(Stream content, string originalFileName, string contentType, string caseFolder, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StoredFileResult("virtual-path", originalFileName, "checksum", 100));

        public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(null);
    }
}
