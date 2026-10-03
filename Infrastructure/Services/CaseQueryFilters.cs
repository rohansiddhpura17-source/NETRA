using NETRA.Application.Analytics;
using NETRA.Domain.Cases;
using NETRA.Domain.Enums;

namespace NETRA.Infrastructure.Services;

/// <summary>
/// The single place where dashboard, analytics, risk and map modules turn filter
/// criteria into a case query, so every module filters identically.
/// </summary>
public static class CaseQueryFilters
{
    public static IQueryable<CrimeCase> Apply(
        IQueryable<CrimeCase> query,
        AnalyticsFilterCriteria criteria,
        DateTime dateFrom,
        DateTime dateTo)
    {
        var toExclusive = dateTo.AddDays(1);
        query = query.Where(c => c.IncidentDate >= dateFrom && c.IncidentDate < toExclusive);

        if (criteria.DistrictId is { } districtId)
        {
            query = query.Where(c => c.CaseLocations.Any(cl => cl.Location!.DistrictId == districtId));
        }

        if (criteria.CrimeTypeId is { } crimeTypeId)
        {
            query = query.Where(c => c.CrimeTypeId == crimeTypeId);
        }

        if (Enum.TryParse<CaseSeverity>(criteria.Severity, ignoreCase: true, out var severity))
        {
            query = query.Where(c => c.Severity == severity);
        }

        return query;
    }
}
