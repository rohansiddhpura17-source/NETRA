using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NETRA.Domain.Cases;
using NETRA.Domain.Geography;
using NETRA.Infrastructure.Data;
using NETRA.Web.Models.Intake;
using Xunit;

namespace NETRA.Tests.Web;

public sealed class IntakeWebhookTests : IClassFixture<NetraWebFactory>
{
    private readonly NetraWebFactory _factory;

    public IntakeWebhookTests(NetraWebFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Ping_ReturnsOnlineStatus()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/intake/ping");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("NETRA Incident Intake Webhook");
        content.Should().Contain("Online");
    }

    [Fact]
    public async Task IngestGoogleForm_WhenServerTokenIsNotConfigured_ReturnsServiceUnavailable()
    {
        using var factory = new NetraWebFactory(webhookToken: null);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/intake/webhook",
            new GoogleFormIntakePayload
            {
                Title = "Configuration test submission"
            });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task IngestGoogleForm_MissingToken_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/intake/webhook",
            new GoogleFormIntakePayload { Title = "Rejected test submission" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task IngestGoogleForm_InvalidToken_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            "X-NETRA-WEBHOOK-TOKEN", "incorrect-test-token");

        var response = await client.PostAsJsonAsync(
            "/api/intake/webhook",
            new GoogleFormIntakePayload { Title = "Rejected test submission" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task IngestGoogleForm_ValidPayload_CreatesCaseAndAlert()
    {
        // Ensure test reference data exists
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<NetraDbContext>();
            if (!await context.CrimeTypes.AnyAsync())
            {
                context.CrimeTypes.Add(new CrimeType { Name = "Theft", Code = "THF", IsActive = true, DisplayOrder = 1 });
            }
            if (!await context.Districts.AnyAsync())
            {
                context.Districts.Add(new District { Name = "Central District", Code = "CD", IsActive = true });
            }
            await context.SaveChangesAsync();
        }

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add(
            "X-NETRA-WEBHOOK-TOKEN", NetraWebFactory.TestWebhookToken);

        var payload = new GoogleFormIntakePayload
        {
            Title = "Break-in reported at commercial complex",
            CrimeType = "Theft",
            Severity = "High",
            District = "Central District",
            Address = "Sector 4, Main Commercial Street",
            Latitude = 19.0760,
            Longitude = 72.8777,
            IncidentDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            Description = "Witness spotted 2 individuals forcibly entering rear shutter.",
            ModusOperandi = "Crowbar used on roller lock, getaway on dark scooter",
            SuspectDetails = "Two males, dark hoodies, license plate obscured",
            SubmitterRole = "Citizen Informant",
            SubmitterContact = "witness@example.com"
        };

        var response = await client.PostAsJsonAsync("/api/intake/webhook", payload);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<GoogleFormIntakeResponse>();

        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.CaseId.Should().NotBeNull();
        result.CaseNumber.Should().NotBeNullOrWhiteSpace();
        result.AlertId.Should().NotBeNull();

        // Verify database persistence
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<NetraDbContext>();
            var savedCase = await context.CrimeCases.Include(c => c.CrimeType).FirstOrDefaultAsync(c => c.Id == result.CaseId);
            savedCase.Should().NotBeNull();
            savedCase!.Title.Should().Be(payload.Title);
            savedCase.Description.Should().Contain("Witness spotted 2 individuals");
            savedCase.Description.Should().Contain("witness@example.com");

            var savedAlert = await context.Alerts.FirstOrDefaultAsync(a => a.Id == result.AlertId);
            savedAlert.Should().NotBeNull();
            savedAlert!.CrimeCaseId.Should().Be(result.CaseId);
        }
    }
}
