namespace NETRA.Application.Dashboard;

/// <summary>
/// Service contract for loading verified historical public crime statistics (e.g. NCRB reports).
/// These aggregate statistics are maintained independently of operational crime cases and alerts.
/// </summary>
public interface IPublicCrimeStatsService
{
    Task<PublicCrimeStatsDto> GetGujaratCybercrimeStatsAsync(CancellationToken cancellationToken = default);
}
