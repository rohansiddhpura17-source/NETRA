using System.ComponentModel.DataAnnotations;
using NETRA.Application.CaseOperations;

namespace NETRA.Web.Models.ViewModels.Cases;

/// <summary>
/// Investigation workspace for a single case. Deliberately scoped to the modules built
/// in Phase 4; network, AI and image panels are not included.
/// </summary>
public sealed class InvestigationWorkspaceViewModel
{
    public Guid InvestigationId { get; init; }

    public Guid CaseId { get; init; }

    public CaseDetailDto Case { get; init; } = new();

    public IReadOnlyList<CaseChangeDto> History { get; init; } = Array.Empty<CaseChangeDto>();

    // Phase 4 panels
    public IReadOnlyList<CasePersonDto> Victims { get; init; } = Array.Empty<CasePersonDto>();

    public IReadOnlyList<CasePersonDto> Suspects { get; init; } = Array.Empty<CasePersonDto>();

    public IReadOnlyList<CasePersonDto> PersonsOfInterest { get; init; } = Array.Empty<CasePersonDto>();

    public IReadOnlyList<CasePersonDto> OtherPersons { get; init; } = Array.Empty<CasePersonDto>();

    public IReadOnlyList<CaseLocationDto> SceneLocations { get; init; } = Array.Empty<CaseLocationDto>();

    public IReadOnlyList<CaseLocationDto> RelatedLocations { get; init; } = Array.Empty<CaseLocationDto>();

    public bool CanEdit { get; init; }

    public bool CanManageEvidence { get; init; }

    public bool CanManageNotes { get; init; }

    public bool CanManageTimeline { get; init; }
}