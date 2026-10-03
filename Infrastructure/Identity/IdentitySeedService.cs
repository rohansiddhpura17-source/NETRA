using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NETRA.Domain.Security;
using NETRA.Domain.Geography;
using NETRA.Domain.Cases;

namespace NETRA.Infrastructure.Identity;

public class IdentitySeedService
{
    public const string AdminPasswordConfigKey = "DevelopmentSeed:AdminPassword";
    public const string AdminPasswordEnvVar = "NETRA_DEV_ADMIN_PASSWORD";

    private readonly ILogger<IdentitySeedService> _logger;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly NETRA.Infrastructure.Data.NetraDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly DemoCaseDataSeeder _demoCases;

    public IdentitySeedService(
        ILogger<IdentitySeedService> logger,
        RoleManager<IdentityRole<Guid>> roleManager,
        UserManager<ApplicationUser> userManager,
        NETRA.Infrastructure.Data.NetraDbContext context,
        IConfiguration configuration,
        IHostEnvironment environment,
        DemoCaseDataSeeder demoCases)
    {
        _logger = logger;
        _roleManager = roleManager;
        _userManager = userManager;
        _context = context;
        _configuration = configuration;
        _environment = environment;
        _demoCases = demoCases;
    }

    public async Task SeedAsync()
    {
        await SeedRolesAsync();

        if (_environment.IsDevelopment())
        {
            var password = await SeedDevelopmentAdminAsync();

            await SeedDemoDataAsync();
            await SeedDemoCasesAsync(password);
        }
        else
        {
            _logger.LogInformation(
                "Environment is {Environment}; skipping development admin, demo data and demo cases.",
                _environment.EnvironmentName);
        }
    }

    /// <summary>
    /// Returns the configured development password, or null when it is not set.
    /// The same value is reused for demo investigators so no second credential exists.
    /// </summary>
    private string? GetDevelopmentAdminPassword()
    {
        var envPassword = Environment.GetEnvironmentVariable(AdminPasswordEnvVar);
        if (!string.IsNullOrWhiteSpace(envPassword))
        {
            return envPassword;
        }

        return _configuration[AdminPasswordConfigKey];
    }

    private async Task SeedDemoCasesAsync(string? password)
    {
        if (password is null)
        {
            _logger.LogWarning(
                "Demo case data NOT seeded: no development password configured. " +
                "Set {ConfigKey} or {EnvVar} to enable it.",
                AdminPasswordConfigKey, AdminPasswordEnvVar);
            return;
        }

        await _demoCases.SeedAsync(password);
    }

    private async Task SeedRolesAsync()
    {
        var roles = new[]
        {
            "System Administrator",
            "Police / Intelligence Analyst",
            "Investigator",
            "Senior Officer"
        };

        foreach (var role in roles)
        {
            if (!await _roleManager.RoleExistsAsync(role))
            {
                var identityRole = new IdentityRole<Guid> { Name = role, NormalizedName = role.ToUpperInvariant() };
                var result = await _roleManager.CreateAsync(identityRole);
                if (result.Succeeded)
                {
                    _logger.LogInformation("Created role: {Role}", role);
                }
                else
                {
                    _logger.LogError("Failed to create role {Role}: {Errors}", role, string.Join(", ", result.Errors.Select(e => e.Description)));
                }
            }
        }
    }

    private async Task<string?> SeedDevelopmentAdminAsync()
    {
        const string adminEmail = "admin@netra.local";

        var password = GetDevelopmentAdminPassword();
        if (string.IsNullOrWhiteSpace(password))
        {
            _logger.LogWarning(
                "Development admin user was NOT created: no password configured. " +
                "Set configuration key {ConfigKey} (e.g. in appsettings.Development.json) " +
                "or environment variable {EnvVar} to enable it.",
                AdminPasswordConfigKey, AdminPasswordEnvVar);
            return null;
        }

        var existing = await _userManager.FindByEmailAsync(adminEmail);
        if (existing != null)
        {
            _logger.LogInformation("Development admin user already exists; no action taken.");
            return password;
        }

        var admin = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            FirstName = "System",
            LastName = "Administrator",
            BadgeNumber = "ADMIN001",
            Rank = "System Administrator",
            EmailConfirmed = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(admin, password);
        if (result.Succeeded)
        {
            await _userManager.AddToRoleAsync(admin, "System Administrator");
            _logger.LogInformation("Created development admin user: {Email}", adminEmail);
        }
        else
        {
            _logger.LogError(
                "Failed to create development admin user: {Errors}",
                string.Join(", ", result.Errors.Select(e => e.Description)));
        }

        return password;
    }

    private async Task SeedDemoDataAsync()
    {
        if (!await _context.Districts.AnyAsync())
        {
            var districts = new[]
            {
                new District { Name = "Central District", Code = "CD", Description = "Central business district", IsActive = true, CreatedAt = DateTime.UtcNow },
                new District { Name = "North District", Code = "ND", Description = "Northern residential area", IsActive = true, CreatedAt = DateTime.UtcNow },
                new District { Name = "South District", Code = "SD", Description = "Southern industrial zone", IsActive = true, CreatedAt = DateTime.UtcNow },
                new District { Name = "East District", Code = "ED", Description = "Eastern suburbs", IsActive = true, CreatedAt = DateTime.UtcNow },
                new District { Name = "West District", Code = "WD", Description = "Western commercial area", IsActive = true, CreatedAt = DateTime.UtcNow }
            };

            await _context.Districts.AddRangeAsync(districts);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Seeded {Count} districts", districts.Length);
        }

        if (!await _context.PoliceStations.AnyAsync())
        {
            var centralDistrict = await _context.Districts.FirstOrDefaultAsync(d => d.Code == "CD");
            var northDistrict = await _context.Districts.FirstOrDefaultAsync(d => d.Code == "ND");
            var southDistrict = await _context.Districts.FirstOrDefaultAsync(d => d.Code == "SD");

            if (centralDistrict == null || northDistrict == null || southDistrict == null)
            {
                _logger.LogWarning("Police station seeding skipped: expected demo districts (CD/ND/SD) not found.");
            }
            else
            {
                var stations = new[]
                {
                    new PoliceStation { Name = "Central HQ", Code = "CHQ", Address = "100 Main St", PhoneNumber = "555-0100", Email = "chq@netra.local", DistrictId = centralDistrict.Id, IsActive = true, CreatedAt = DateTime.UtcNow },
                    new PoliceStation { Name = "North Precinct", Code = "NP", Address = "200 North Ave", PhoneNumber = "555-0200", Email = "np@netra.local", DistrictId = northDistrict.Id, IsActive = true, CreatedAt = DateTime.UtcNow },
                    new PoliceStation { Name = "South Precinct", Code = "SP", Address = "300 South Blvd", PhoneNumber = "555-0300", Email = "sp@netra.local", DistrictId = southDistrict.Id, IsActive = true, CreatedAt = DateTime.UtcNow }
                };

                await _context.PoliceStations.AddRangeAsync(stations);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Seeded {Count} police stations", stations.Length);
            }
        }

        if (!await _context.CrimeTypes.AnyAsync())
        {
            var crimeTypes = new[]
            {
                new CrimeType { Name = "Homicide", Code = "HOM", Description = "Unlawful killing of another person", DisplayOrder = 1, IsActive = true, CreatedAt = DateTime.UtcNow },
                new CrimeType { Name = "Assault", Code = "ASL", Description = "Physical attack or threat of attack", DisplayOrder = 2, IsActive = true, CreatedAt = DateTime.UtcNow },
                new CrimeType { Name = "Robbery", Code = "ROB", Description = "Taking property by force or threat", DisplayOrder = 3, IsActive = true, CreatedAt = DateTime.UtcNow },
                new CrimeType { Name = "Burglary", Code = "BUR", Description = "Unlawful entry with intent to commit crime", DisplayOrder = 4, IsActive = true, CreatedAt = DateTime.UtcNow },
                new CrimeType { Name = "Theft", Code = "THF", Description = "Unlawful taking of property", DisplayOrder = 5, IsActive = true, CreatedAt = DateTime.UtcNow },
                new CrimeType { Name = "Fraud", Code = "FRD", Description = "Deception for personal gain", DisplayOrder = 6, IsActive = true, CreatedAt = DateTime.UtcNow },
                new CrimeType { Name = "Drug Offenses", Code = "DRG", Description = "Illegal drug possession, distribution, manufacturing", DisplayOrder = 7, IsActive = true, CreatedAt = DateTime.UtcNow },
                new CrimeType { Name = "Traffic Violations", Code = "TRF", Description = "Traffic-related offenses", DisplayOrder = 8, IsActive = true, CreatedAt = DateTime.UtcNow }
            };

            await _context.CrimeTypes.AddRangeAsync(crimeTypes);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Seeded {Count} crime types", crimeTypes.Length);
        }
    }
}