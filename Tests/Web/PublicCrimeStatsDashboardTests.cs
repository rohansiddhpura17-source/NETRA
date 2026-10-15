using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NETRA.Infrastructure.Data;
using Xunit;

namespace NETRA.Tests.Web;

public class PublicCrimeStatsDashboardTests : IClassFixture<NetraWebFactory>
{
    private const string Password = "ValidPassw0rd!";
    private readonly NetraWebFactory _factory;

    public PublicCrimeStatsDashboardTests(NetraWebFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> SignInAsync(string email, string role)
    {
        await _factory.SeedUserAsync(email, Password, role);

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var token = AuthenticationScreenTests.ExtractAntiForgeryToken(
            await client.GetStringAsync("/Account/Login"));

        var response = await client.PostAsync("/Account/Login", AuthenticationScreenTests.Form(
            ("UserName", email),
            ("Password", Password),
            ("__RequestVerificationToken", token)));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return client;
    }

    [Fact]
    public async Task Dashboard_RendersPublicCrimeIntelligenceSection_WithAllCardsChartsAndCitations()
    {
        var client = await SignInAsync("publicstats.analyst@netra.local", "Police / Intelligence Analyst");

        int initialCaseCount;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<NetraDbContext>();
            initialCaseCount = await context.CrimeCases.CountAsync();
        }

        var response = await client.GetAsync("/Dashboard");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var html = await response.Content.ReadAsStringAsync();

        // 1. Heading and Metadata
        html.Should().Contain("Public Crime Intelligence");
        html.Should().Contain("Gujarat Cybercrime Statistics");
        html.Should().Contain("NCRB Crime in India 2023");
        html.Should().Contain("Times of India");
        html.Should().Contain("Historical aggregate statistics");
        html.Should().Contain("2021 - 2023");
        html.Should().Contain("https://timesofindia.indiatimes.com/city/ahmedabad/cybercrimes-rose-41-in-guj-ncrb/articleshow/124324670.cms");

        // 2. Stat Cards
        html.Should().Contain("Gujarat 2023 Cybercrime");
        html.Should().Contain("1,995");
        html.Should().Contain("+40.8%");

        html.Should().Contain("Cheating / Financial Fraud");
        html.Should().Contain("1,034");

        html.Should().Contain("Ahmedabad (2023)");
        html.Should().Contain("246");

        html.Should().Contain("Surat (2023)");
        html.Should().Contain("397");

        // 3. Charge-Sheeting Rates
        html.Should().Contain("52.1%");
        html.Should().Contain("33.9%");

        // 4. SVG Charts rendered
        html.Should().Contain("netra-chart--bar");
        html.Should().Contain("netra-chart--donut");
        html.Should().Contain("Gujarat State Cybercrime Totals (2022 vs 2023)");
        html.Should().Contain("Cyber Cheating &amp; Online Fraud Growth");
        html.Should().Contain("City Comparison: Ahmedabad vs Surat (2021 vs 2023)");
        html.Should().Contain("Reported Cybercrime Motives (2023)");

        // 5. Motive Breakdown values
        html.Should().Contain("1,187"); // Fraud
        html.Should().Contain("387");   // Disrepute
        html.Should().Contain("183");   // Extortion

        // 6. Strict No-JavaScript policy verified
        html.ToLowerInvariant().Should().NotContain("<script");
        html.ToLowerInvariant().Should().NotContain("onclick=");
        html.ToLowerInvariant().Should().NotContain("javascript:");

        // 7. Verify operational database was not mutated or polluted
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<NetraDbContext>();
            var currentCaseCount = await context.CrimeCases.CountAsync();
            currentCaseCount.Should().Be(initialCaseCount, "public historical statistics must not create operational cases");
        }
    }
}
