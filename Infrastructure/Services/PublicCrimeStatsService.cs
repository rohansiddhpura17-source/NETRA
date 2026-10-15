using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using NETRA.Application.Dashboard;

namespace NETRA.Infrastructure.Services;

/// <summary>
/// Loads verified historical public crime statistics (e.g., NCRB reports).
/// These figures represent official state-level aggregate statistics and are strictly
/// decoupled from active operational crime cases, FIRs, and alerts.
/// </summary>
public sealed class PublicCrimeStatsService : IPublicCrimeStatsService
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<PublicCrimeStatsService> _logger;
    private PublicCrimeStatsDto? _cached;

    public PublicCrimeStatsService(
        IWebHostEnvironment env,
        ILogger<PublicCrimeStatsService> logger)
    {
        _env = env;
        _logger = logger;
    }

    public async Task<PublicCrimeStatsDto> GetGujaratCybercrimeStatsAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(_env.WebRootPath))
        {
            candidates.Add(Path.Combine(_env.WebRootPath, "data", "gujarat-cybercrime-2023.json"));
        }

        if (!string.IsNullOrWhiteSpace(_env.ContentRootPath))
        {
            candidates.Add(Path.Combine(_env.ContentRootPath, "wwwroot", "data", "gujarat-cybercrime-2023.json"));
        }

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "wwwroot", "data", "gujarat-cybercrime-2023.json"));
        candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), "Web", "wwwroot", "data", "gujarat-cybercrime-2023.json"));
        candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "data", "gujarat-cybercrime-2023.json"));

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                try
                {
                    await using var stream = File.OpenRead(path);
                    var dto = await JsonSerializer.DeserializeAsync<PublicCrimeStatsDto>(
                        stream,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                        cancellationToken);

                    if (dto is not null)
                    {
                        _cached = dto;
                        return dto;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read public crime statistics JSON from {Path}", path);
                }
            }
        }

        _logger.LogInformation("Using verified fallback historical NCRB Gujarat cybercrime statistics.");
        _cached = GetFallbackStats();
        return _cached;
    }

    private static PublicCrimeStatsDto GetFallbackStats() => new()
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
}
