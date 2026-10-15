using System.ComponentModel.DataAnnotations;
using NETRA.Application.CaseOperations;
using NETRA.Web.Models.Ui;
using NETRA.Domain.Enums;

namespace NETRA.Web.Models.ViewModels.Cases;

public sealed class CaseFilterViewModel
{
    public string? Search { get; set; }

    public Guid? CrimeTypeId { get; set; }

    public Guid? DistrictId { get; set; }

    public string? Status { get; set; }

    public string? Severity { get; set; }

    [DataType(DataType.Date)]
    public DateTime? IncidentDateFrom { get; set; }

    [DataType(DataType.Date)]
    public DateTime? IncidentDateTo { get; set; }

    public string SortBy { get; set; } = CaseSortOptions.NewestFirst;

    public int Page { get; set; } = 1;

    public CaseFilterCriteria ToCriteria(int pageSize = 25) => new()
    {
        Search = Search,
        CrimeTypeId = CrimeTypeId,
        DistrictId = DistrictId,
        Status = Status,
        Severity = Severity,
        IncidentDateFrom = IncidentDateFrom,
        IncidentDateTo = IncidentDateTo,
        SortBy = SortBy,
        Page = Page,
        PageSize = pageSize
    };
}

public sealed class CaseListViewModel
{
    public CaseListViewModel(CaseListResultDto result, CaseFilterViewModel filter, CaseLookupDto lookups)
    {
        Result = result;
        Filter = filter;
        Lookups = lookups;
    }

    public CaseListResultDto Result { get; }

    public CaseFilterViewModel Filter { get; }

    public CaseLookupDto Lookups { get; }

    public IReadOnlyList<CaseListItemViewModel> Items =>
        Result.Page.Items.Select(i => new CaseListItemViewModel(i)).ToList();

    public int TotalCount => Result.Page.TotalCount;

    public int Page => Result.Page.Page;

    public int PageSize => Result.Page.PageSize;

    public int TotalPages => Result.Page.TotalPages;

    public int From => Result.Page.From;

    public int To => Result.Page.To;

    public bool HasFilters => Filter.ToCriteria().HasAnyFilter;

    /// <summary>Status values offered in the filter, derived from the enum.</summary>
    public static IReadOnlyList<SelectOption> StatusOptions { get; } =
        Enum.GetValues<CaseStatus>().Select(s => new SelectOption(s.ToString(), NetraDisplay.Words(s.ToString()))).ToList();

    public static IReadOnlyList<SelectOption> SeverityOptions { get; } =
        Enum.GetValues<CaseSeverity>().Select(s => new SelectOption(s.ToString(), s.ToString())).ToList();

    public static IReadOnlyList<SelectOption> SortOptions { get; } =
    [
        new(CaseSortOptions.NewestFirst, "Newest incident first"),
        new(CaseSortOptions.OldestFirst, "Oldest incident first"),
        new(CaseSortOptions.CaseNumberAsc, "Case number (A–Z)"),
        new(CaseSortOptions.SeverityDesc, "Highest severity first")
    ];
}

public sealed record SelectOption(string Value, string Label);

public sealed class CaseListItemViewModel
{
    public CaseListItemViewModel(CaseListItemDto dto)
    {
        Id = dto.Id;
        CaseNumber = dto.CaseNumber;
        Title = dto.Title;
        CrimeTypeName = dto.CrimeTypeName;
        IncidentDate = dto.IncidentDate;
        LastUpdatedDate = dto.LastUpdatedDate;
        DistrictName = dto.DistrictName ?? "—";
        Status = dto.Status;
        Severity = dto.Severity;
        InvestigatorName = dto.InvestigatorName ?? "Unassigned";
        PersonCount = dto.PersonCount;
        EvidenceCount = dto.EvidenceCount;
    }

    public Guid Id { get; }
    public string CaseNumber { get; }
    public string Title { get; }
    public string CrimeTypeName { get; }
    public DateTime IncidentDate { get; }
    public DateTime? LastUpdatedDate { get; }
    public string DistrictName { get; }
    public string Status { get; }
    public string Severity { get; }
    public string InvestigatorName { get; }
    public int PersonCount { get; }
    public int EvidenceCount { get; }

    public bool IsSyntheticDemo =>
        Title.Contains("[DEMO", StringComparison.OrdinalIgnoreCase) ||
        CaseNumber.StartsWith("CC-2026-00", StringComparison.OrdinalIgnoreCase);

    public NetraTone StatusTone => Status switch
    {
        nameof(CaseStatus.Open) => NetraTone.Info,
        nameof(CaseStatus.UnderInvestigation) => NetraTone.Primary,
        nameof(CaseStatus.PendingReview) => NetraTone.Warning,
        nameof(CaseStatus.Closed) => NetraTone.Success,
        nameof(CaseStatus.Archived) => NetraTone.Neutral,
        nameof(CaseStatus.Reopened) => NetraTone.Danger,
        _ => NetraTone.Neutral
    };

    public NetraTone SeverityTone => Severity switch
    {
        nameof(CaseSeverity.Critical) => NetraTone.Danger,
        nameof(CaseSeverity.High) => NetraTone.Warning,
        nameof(CaseSeverity.Medium) => NetraTone.Info,
        nameof(CaseSeverity.Low) => NetraTone.Success,
        _ => NetraTone.Neutral
    };
}