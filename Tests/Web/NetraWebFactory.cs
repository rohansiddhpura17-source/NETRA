using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NETRA.Infrastructure.Data;

namespace NETRA.Tests.Web;

/// <summary>
/// Boots the real NETRA MVC application with the SQL Server provider swapped for an
/// in-memory database, so UI behaviour can be verified without a database server.
/// Database initialisation is disabled; failures are not swallowed.
/// </summary>
public class NetraWebFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.UseSetting("Database:AutoMigrate", "false");

        // Keep test uploads out of the source tree: evidence and investigation images
        // land in a per-factory temporary directory instead of App_Data.
        builder.UseSetting(
            "FileStorage:RootPath",
            Path.Combine(Path.GetTempPath(), $"netra-web-storage-{_databaseName:N}"));

        builder.ConfigureTestServices(services =>
        {
            // Remove the SQL Server registration so only the in-memory provider remains.
            // Removing DbContextOptions alone is not enough: EF Core also stores the
            // provider in its options-configuration services.
            services.RemoveAll<DbContextOptions<NetraDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<IDbContextOptionsConfiguration<NetraDbContext>>();
            services.RemoveAll<NetraDbContext>();

            services.AddDbContext<NetraDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));
        });
    }

    private readonly SemaphoreSlim _referenceDataLock = new(1, 1);
    private bool _referenceDataSeeded;

    /// <summary>
    /// Seeds the minimal reference data the case module needs (crime types, districts,
    /// police stations). Runs once per factory. This is test infrastructure: the
    /// application under test still has <c>Database:AutoMigrate</c> disabled.
    /// </summary>
    public async Task SeedReferenceDataAsync()
    {
        if (_referenceDataSeeded)
        {
            return;
        }

        await _referenceDataLock.WaitAsync();
        try
        {
            if (_referenceDataSeeded)
            {
                return;
            }

            using var scope = Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<NetraDbContext>();

            if (!await context.CrimeTypes.AnyAsync())
            {
                context.CrimeTypes.AddRange(
                    new NETRA.Domain.Cases.CrimeType
                    {
                        Name = "Theft", Code = "THF", DisplayOrder = 1, IsActive = true
                    },
                    new NETRA.Domain.Cases.CrimeType
                    {
                        Name = "Assault", Code = "ASL", DisplayOrder = 2, IsActive = true
                    });
            }

            if (!await context.Districts.AnyAsync())
            {
                context.Districts.AddRange(
                    new NETRA.Domain.Geography.District
                    {
                        Name = "Central District", Code = "CD", IsActive = true
                    },
                    new NETRA.Domain.Geography.District
                    {
                        Name = "North District", Code = "ND", IsActive = true
                    });
            }

            // Crime types and districts must be persisted first: the police station
            // below needs a real district id.
            await context.SaveChangesAsync();

            if (!await context.PoliceStations.AnyAsync())
            {
                var central = await context.Districts.FirstAsync(d => d.Code == "CD");

                context.PoliceStations.Add(new NETRA.Domain.Geography.PoliceStation
                {
                    Name = "Central HQ", Code = "CHQ", DistrictId = central.Id, IsActive = true
                });
            }

            // Saved after all lookups so the police station can reference a district id.
            await context.SaveChangesAsync();
            _referenceDataSeeded = true;
        }
        finally
        {
            _referenceDataLock.Release();
        }
    }

    /// <summary>
    /// Seeds a user with the given roles. Idempotent, so repeated calls from the same
    /// in-memory database are safe. Returns the account password unchanged.
    /// </summary>
    public async Task SeedUserAsync(string email, string password, params string[] roles)
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider
            .GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<NETRA.Domain.Security.ApplicationUser>>();
        var roleManager = scope.ServiceProvider
            .GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<Microsoft.AspNetCore.Identity.IdentityRole<Guid>>>();

        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(
                    new Microsoft.AspNetCore.Identity.IdentityRole<Guid> { Name = role });
            }
        }

        var existing = await userManager.FindByEmailAsync(email);

        if (existing is not null)
        {
            // Already seeded: make sure the password and roles are what the test expects.
            var token = await userManager.GeneratePasswordResetTokenAsync(existing);
            var reset = await userManager.ResetPasswordAsync(existing, token, password);
            if (!reset.Succeeded)
            {
                throw new InvalidOperationException(
                    "Failed to reset test user password: " +
                    string.Join(", ", reset.Errors.Select(e => e.Description)));
            }

            foreach (var role in roles)
            {
                if (!await userManager.IsInRoleAsync(existing, role))
                {
                    await userManager.AddToRoleAsync(existing, role);
                }
            }

            return;
        }

        var user = new NETRA.Domain.Security.ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = "Test",
            LastName = "User",
            EmailConfirmed = true,
            IsActive = true
        };

        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Failed to seed test user: " +
                string.Join(", ", result.Errors.Select(e => e.Description)));
        }

        foreach (var role in roles)
        {
            await userManager.AddToRoleAsync(user, role);
        }
    }
}