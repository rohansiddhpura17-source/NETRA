using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NETRA.Domain.Geography;
using NETRA.Domain.Cases;
using NETRA.Infrastructure.Data;
using NETRA.Infrastructure.Identity;
using FluentAssertions;
using Xunit;

namespace NETRA.Tests.Infrastructure;

public class IdentitySeedServiceTests : IDisposable
{
    private readonly NetraDbContext _context;
    private readonly UserManager<NETRA.Domain.Security.ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly DemoCaseDataSeeder _demoCases;

    public IdentitySeedServiceTests()
    {
        var options = new DbContextOptionsBuilder<NetraDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new NetraDbContext(options);
        _userManager = new UserManager<NETRA.Domain.Security.ApplicationUser>(
            new InMemoryUserStore(_context),
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!);
        _roleManager = new RoleManager<IdentityRole<Guid>>(
            new InMemoryRoleStore(_context),
            null!,
            null!,
            null!,
            null!);
        _demoCases = new DemoCaseDataSeeder(_context, _userManager, NullLogger<DemoCaseDataSeeder>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        Environment.SetEnvironmentVariable(IdentitySeedService.AdminPasswordEnvVar, null);
    }

    private IdentitySeedService CreateService(
        bool isDevelopment,
        string? configuredPassword = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [IdentitySeedService.AdminPasswordConfigKey] = configuredPassword
            })
            .Build();

        var environment = new FakeHostEnvironment { EnvironmentName = isDevelopment ? Environments.Development : Environments.Production };

        return new IdentitySeedService(
            NullLogger<IdentitySeedService>.Instance,
            _roleManager,
            _userManager,
            _context,
            configuration,
            environment,
            _demoCases);
    }

    [Fact]
    public async Task SeedAsync_ShouldCreateRolesIdempotently()
    {
        var service = CreateService(isDevelopment: false);

        await service.SeedAsync();
        var rolesAfterFirstRun = _roleManager.Roles.Count();

        await service.SeedAsync();
        var rolesAfterSecondRun = _roleManager.Roles.Count();

        rolesAfterFirstRun.Should().Be(4);
        rolesAfterSecondRun.Should().Be(4);
    }

    [Fact]
    public async Task SeedAsync_ShouldCreateRolesInProduction()
    {
        var service = CreateService(isDevelopment: false);

        await service.SeedAsync();

        _roleManager.Roles.Select(r => r.Name).Should().BeEquivalentTo(new[]
        {
            "System Administrator",
            "Police / Intelligence Analyst",
            "Investigator",
            "Senior Officer"
        });
    }

    [Fact]
    public async Task SeedAsync_ShouldNotCreateAdminUserWhenPasswordNotConfigured()
    {
        var service = CreateService(isDevelopment: true, configuredPassword: null);

        await service.SeedAsync();

        (await _userManager.FindByEmailAsync("admin@netra.local")).Should().BeNull();
    }

    [Fact]
    public async Task SeedAsync_ShouldNotCreateAdminUserWhenPasswordIsWhitespace()
    {
        var service = CreateService(isDevelopment: true, configuredPassword: "   ");

        await service.SeedAsync();

        (await _userManager.FindByEmailAsync("admin@netra.local")).Should().BeNull();
    }

    [Fact]
    public async Task SeedAsync_ShouldNotCreateDemoDataInProduction()
    {
        var service = CreateService(isDevelopment: false);

        await service.SeedAsync();

        (await _context.Districts.CountAsync()).Should().Be(0);
        (await _context.PoliceStations.CountAsync()).Should().Be(0);
        (await _context.CrimeTypes.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SeedAsync_ShouldNotCreateAdminUserInProduction_EvenIfPasswordConfigured()
    {
        Environment.SetEnvironmentVariable(IdentitySeedService.AdminPasswordEnvVar, "Str0ng!Passw0rd#2026");
        var service = CreateService(isDevelopment: false);

        await service.SeedAsync();

        (await _userManager.FindByEmailAsync("admin@netra.local")).Should().BeNull();
    }

    [Fact]
    public async Task SeedAsync_ShouldSeedDemoDataInDevelopment()
    {
        var service = CreateService(isDevelopment: true, configuredPassword: null);

        await service.SeedAsync();

        (await _context.Districts.CountAsync()).Should().Be(5);
        (await _context.PoliceStations.CountAsync()).Should().Be(3);
        (await _context.CrimeTypes.CountAsync()).Should().Be(8);
    }

    [Fact]
    public async Task SeedAsync_ShouldSeedDemoDataIdempotently()
    {
        var service = CreateService(isDevelopment: true, configuredPassword: null);

        await service.SeedAsync();
        await service.SeedAsync();

        (await _context.Districts.CountAsync()).Should().Be(5);
        (await _context.PoliceStations.CountAsync()).Should().Be(3);
        (await _context.CrimeTypes.CountAsync()).Should().Be(8);
    }

    [Fact]
    public void AdminPasswordConfigKey_ShouldNotHaveHardCodedFallback()
    {
        IdentitySeedService.AdminPasswordConfigKey.Should().Be("DevelopmentSeed:AdminPassword");
        IdentitySeedService.AdminPasswordEnvVar.Should().Be("NETRA_DEV_ADMIN_PASSWORD");
    }
}

internal sealed class FakeHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "NETRA.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
}

internal sealed class InMemoryUserStore : Microsoft.AspNetCore.Identity.EntityFrameworkCore.UserStore<NETRA.Domain.Security.ApplicationUser, IdentityRole<Guid>, NetraDbContext, Guid>
{
    public InMemoryUserStore(NetraDbContext context) : base(context)
    {
    }
}

internal sealed class InMemoryRoleStore : Microsoft.AspNetCore.Identity.EntityFrameworkCore.RoleStore<IdentityRole<Guid>, NetraDbContext, Guid>
{
    public InMemoryRoleStore(NetraDbContext context) : base(context)
    {
    }
}