namespace NETRA.Application.Analytics;

public interface IAnalyticsService
{
    Task<AnalyticsReportDto> GetReportAsync(
        AnalyticsFilterCriteria criteria,
        CancellationToken cancellationToken = default);

    Task<AnalyticsLookupDto> GetLookupsAsync(CancellationToken cancellationToken = default);
}
