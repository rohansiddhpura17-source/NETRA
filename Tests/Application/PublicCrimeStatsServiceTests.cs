using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using NETRA.Application.Dashboard;
using NETRA.Infrastructure.Services;
using NETRA.Web.Models.ViewModels.Dashboard;
using Xunit;

namespace NETRA.Tests.Application;

public sealed class PublicCrimeStatsServiceTests
{
    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string ApplicationName { get; set; } = "NETRA.Web";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
    }

    [Fact]
    public async Task GetGujaratCybercrimeStats_ReturnsVerifiedNcrbStatistics()
    {
        var env = new TestWebHostEnvironment();
        var service = new PublicCrimeStatsService(env, NullLogger<PublicCrimeStatsService>.Instance);

        var stats = await service.GetGujaratCybercrimeStatsAsync();

        stats.Should().NotBeNull();
        stats.Title.Should().Be("Gujarat Cybercrime Statistics");
        stats.Report.Should().Be("NCRB Crime in India 2023");
        stats.Geography.Should().Be("Gujarat, India");
        stats.SourceUrl.Should().Contain("timesofindia.indiatimes.com");
        stats.DataType.Should().Be("Historical aggregate statistics");

        // Gujarat totals
        stats.GujaratTotals.Should().HaveCount(2);
        stats.GujaratTotals.First(t => t.Year == 2022).Cases.Should().Be(1417);
        stats.GujaratTotals.First(t => t.Year == 2023).Cases.Should().Be(1995);

        // Cheating cases
        stats.CheatingCases.Should().HaveCount(2);
        stats.CheatingCases.First(t => t.Year == 2022).Cases.Should().Be(575);
        stats.CheatingCases.First(t => t.Year == 2023).Cases.Should().Be(1034);

        // Cities
        stats.AnnualCases.First(c => c.City == "Ahmedabad" && c.Year == 2021).Cases.Should().Be(447);
        stats.AnnualCases.First(c => c.City == "Ahmedabad" && c.Year == 2023).Cases.Should().Be(246);
        stats.AnnualCases.First(c => c.City == "Surat" && c.Year == 2021).Cases.Should().Be(296);
        stats.AnnualCases.First(c => c.City == "Surat" && c.Year == 2023).Cases.Should().Be(397);

        // Motives
        stats.ReportedMotives2023.Should().HaveCount(3);
        stats.ReportedMotives2023.First(m => m.Category == "Fraud").Cases.Should().Be(1187);
        stats.ReportedMotives2023.First(m => m.Category == "Causing disrepute").Cases.Should().Be(387);
        stats.ReportedMotives2023.First(m => m.Category == "Extortion").Cases.Should().Be(183);

        // Charge sheeting rate
        stats.ChargeSheetingRatePercent.Gujarat.Should().Be(52.1m);
        stats.ChargeSheetingRatePercent.National.Should().Be(33.9m);
    }

    [Fact]
    public void PublicCrimeStatsViewModel_BuildsRequiredCardsAndCharts()
    {
        var dto = new PublicCrimeStatsDto
        {
            Title = "Gujarat Cybercrime Statistics",
            Report = "NCRB Crime in India 2023",
            Geography = "Gujarat, India",
            Source = "Times of India, October 6, 2025",
            SourceUrl = "https://timesofindia.indiatimes.com/city/ahmedabad/cybercrimes-rose-41-in-guj-ncrb/articleshow/124324670.cms",
            DataType = "Historical aggregate statistics",
            Years = [2021, 2022, 2023],
            AnnualCases =
            [
                new CityAnnualCasesDto { Year = 2021, City = "Ahmedabad", Cases = 447 },
                new CityAnnualCasesDto { Year = 2023, City = "Ahmedabad", Cases = 246 },
                new CityAnnualCasesDto { Year = 2021, City = "Surat", Cases = 296 },
                new CityAnnualCasesDto { Year = 2023, City = "Surat", Cases = 397 }
            ],
            GujaratTotals =
            [
                new YearlyTotalDto { Year = 2022, Cases = 1417 },
                new YearlyTotalDto { Year = 2023, Cases = 1995 }
            ],
            CheatingCases =
            [
                new YearlyTotalDto { Year = 2022, Cases = 575 },
                new YearlyTotalDto { Year = 2023, Cases = 1034 }
            ],
            ReportedMotives2023 =
            [
                new ReportedMotiveDto { Category = "Fraud", Cases = 1187 },
                new ReportedMotiveDto { Category = "Causing disrepute", Cases = 387 },
                new ReportedMotiveDto { Category = "Extortion", Cases = 183 }
            ],
            ChargeSheetingRatePercent = new ChargeSheetingRateDto
            {
                Gujarat = 52.1m,
                National = 33.9m
            }
        };

        var vm = PublicCrimeStatsViewModel.Create(dto);

        vm.StatCards.Should().HaveCount(5);
        vm.StatCards.Should().Contain(c => c.Label == "Gujarat 2023 Cybercrime" && c.Value == "1,995");
        vm.StatCards.Should().Contain(c => c.Label == "Cheating / Financial Fraud" && c.Value == "1,034");
        vm.StatCards.Should().Contain(c => c.Label == "Ahmedabad (2023)" && c.Value == "246");
        vm.StatCards.Should().Contain(c => c.Label == "Surat (2023)" && c.Value == "397");
        vm.StatCards.Should().Contain(c => c.Label == "Gujarat Charge-Sheet Rate" && c.Value == "52.1%");

        // Verify charts
        vm.AnnualTotalsChart.Should().NotBeNull();
        vm.AnnualTotalsChart!.Categories.Should().Contain("Gujarat (2023)");
        vm.AnnualTotalsChart.Series[0].Values.Should().Contain(1995);

        vm.CheatingComparisonChart.Should().NotBeNull();
        vm.CheatingComparisonChart!.Categories.Should().Contain("Cheating (2023)");
        vm.CheatingComparisonChart.Series[0].Values.Should().Contain(1034);

        vm.CityComparisonChart.Should().NotBeNull();
        vm.CityComparisonChart!.Categories.Should().Contain(["Ahmedabad (2021)", "Ahmedabad (2023)", "Surat (2021)", "Surat (2023)"]);

        vm.MotiveBreakdownChart.Should().NotBeNull();
        vm.MotiveBreakdownChart!.Categories.Should().Contain(["Fraud", "Causing disrepute", "Extortion"]);
    }
}
