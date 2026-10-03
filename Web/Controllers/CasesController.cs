using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NETRA.Application.Abstractions;
using NETRA.Application.CaseOperations;
using NETRA.Domain.Enums;
using NETRA.Web.Authorization;
using NETRA.Web.Models.ViewModels.Cases;

namespace NETRA.Web.Controllers;

/// <summary>
/// Crime case screens. Controllers translate HTTP to application calls and map results
/// to view models; all business rules, validation and persistence live in the
/// application service.
/// </summary>
[Authorize]
[Route("Cases")]
public class CasesController : Controller
{
    private readonly ICaseService _cases;
    private readonly IAuditActorAccessor _actor;
    private readonly IAuthorizationService _authorization;

    public CasesController(
        ICaseService cases,
        IAuditActorAccessor actor,
        IAuthorizationService authorization)
    {
        _cases = cases;
        _actor = actor;
        _authorization = authorization;
    }

    private async Task<bool> HasPolicyAsync(string policy) =>
        (await _authorization.AuthorizeAsync(User, policy)).Succeeded;

    // ------------------------------------------------------------ list

    [HttpGet("")]
    [Authorize(Policy = NetraPolicies.CaseRead)]
    public async Task<IActionResult> Index(
        [FromQuery] CaseFilterViewModel filter,
        CancellationToken cancellationToken)
    {
        if (filter.Page < 1)
        {
            filter.Page = 1;
        }

        var result = await _cases.SearchCasesAsync(filter.ToCriteria(), cancellationToken);
        var lookups = await _cases.GetLookupsAsync(cancellationToken);

        var model = new CaseListViewModel(result, filter, lookups);
        return View(model);
    }

    // ---------------------------------------------------------- create

    [HttpGet("Create")]
    [Authorize(Policy = NetraPolicies.CaseCreate)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var lookups = await _cases.GetLookupsAsync(cancellationToken);

        var model = new CreateCaseViewModel
        {
            CrimeTypes = ToOptions(lookups.CrimeTypes),
            Districts = ToOptions(lookups.Districts),
            Investigators = ToOptions(lookups.Investigators),
            StatusOptions = CaseListViewModel.StatusOptions,
            SeverityOptions = CaseListViewModel.SeverityOptions
        };

        return View(model);
    }

    [HttpPost("Create")]
    [Authorize(Policy = NetraPolicies.CaseCreate)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateCaseViewModel model, CancellationToken cancellationToken)
    {
        var lookups = await _cases.GetLookupsAsync(cancellationToken);

        model.CrimeTypes = ToOptions(lookups.CrimeTypes);
        model.Districts = ToOptions(lookups.Districts);
        model.Investigators = ToOptions(lookups.Investigators);
        model.StatusOptions = CaseListViewModel.StatusOptions;
        model.SeverityOptions = CaseListViewModel.SeverityOptions;

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var request = new CreateCaseRequest
        {
            CaseNumber = model.CaseNumber,
            Title = model.Title,
            Description = model.Description,
            CrimeTypeId = model.CrimeTypeId,
            Status = model.Status,
            Severity = model.Severity,
            IncidentDate = model.IncidentDate,
            DistrictId = model.DistrictId,
            Address = model.Address,
            Latitude = model.Latitude,
            Longitude = model.Longitude,
            InvestigatorId = model.InvestigatorId,
            ModusOperandi = model.ModusOperandi
        };

        var result = await _cases.CreateCaseAsync(request, _actor.GetUserId(User), cancellationToken);

        if (result.Succeeded && result.Value != Guid.Empty)
        {
            TempData["Netra.Success"] = $"Case {model.CaseNumber ?? string.Empty} registered.";
            return RedirectToAction(nameof(Details), new { id = result.Value });
        }

        ApplyErrors(result.Errors);
        return View(model);
    }

    // --------------------------------------------------------- details

    [HttpGet("Details/{id:guid}")]
    [Authorize(Policy = NetraPolicies.CaseRead)]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var detail = await _cases.GetCaseDetailsAsync(id, cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        var lookups = await _cases.GetLookupsAsync(cancellationToken);
        var history = await _cases.GetCaseHistoryAsync(id, cancellationToken);

        var model = new CaseDetailsViewModel
        {
            Case = detail,
            History = history,
            Persons = ToOptions(lookups.Persons),
            Locations = ToOptions(lookups.Locations),
            Investigators = ToOptions(lookups.Investigators),
            CanEdit = await HasPolicyAsync(NetraPolicies.CaseEdit),
            CanManagePersons = await HasPolicyAsync(NetraPolicies.CaseManagePersons),
            CanManageLocations = await HasPolicyAsync(NetraPolicies.CaseManageLocations),
            CanManageEvidence = await HasPolicyAsync(NetraPolicies.CaseManageEvidence),
            CanManageNotes = await HasPolicyAsync(NetraPolicies.CaseManageNotes),
            CanManageTimeline = await HasPolicyAsync(NetraPolicies.CaseManageTimeline),
            CanAssign = await HasPolicyAsync(NetraPolicies.CaseAssign)
        };

        return View(model);
    }

    // ------------------------------------------------------------ edit

    [HttpGet("Edit/{id:guid}")]
    [Authorize(Policy = NetraPolicies.CaseEdit)]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var detail = await _cases.GetCaseDetailsAsync(id, cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        var lookups = await _cases.GetLookupsAsync(cancellationToken);

        var model = new EditCaseViewModel
        {
            Title = detail.Title,
            Description = detail.Description,
            CrimeTypeId = lookups.CrimeTypes.FirstOrDefault(c => c.Label == detail.CrimeTypeName)?.Id ?? Guid.Empty,
            Status = detail.Status,
            Severity = detail.Severity,
            IncidentDate = detail.IncidentDate,
            ModusOperandi = detail.ModusOperandi,
            CrimeTypes = ToOptions(lookups.CrimeTypes),
            StatusOptions = CaseListViewModel.StatusOptions,
            SeverityOptions = CaseListViewModel.SeverityOptions
        };

        ViewBag.CaseId = id;
        ViewBag.CaseNumber = detail.CaseNumber;

        return View(model);
    }

    [HttpPost("Edit/{id:guid}")]
    [Authorize(Policy = NetraPolicies.CaseEdit)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, EditCaseViewModel model, CancellationToken cancellationToken)
    {
        var lookups = await _cases.GetLookupsAsync(cancellationToken);

        model.CrimeTypes = ToOptions(lookups.CrimeTypes);
        model.StatusOptions = CaseListViewModel.StatusOptions;
        model.SeverityOptions = CaseListViewModel.SeverityOptions;

        ViewBag.CaseId = id;

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var request = new UpdateCaseRequest
        {
            Title = model.Title,
            Description = model.Description,
            CrimeTypeId = model.CrimeTypeId,
            Status = model.Status,
            Severity = model.Severity,
            IncidentDate = model.IncidentDate,
            ModusOperandi = model.ModusOperandi
        };

        var result = await _cases.UpdateCaseAsync(id, request, _actor.GetUserId(User), cancellationToken);

        if (result.Succeeded)
        {
            TempData["Netra.Success"] = "Case updated.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (result.Status == OperationStatus.NotFound)
        {
            return NotFound();
        }

        ApplyErrors(result.Errors);
        return View(model);
    }

    // ------------------------------------------------- person association

    [HttpPost("Details/{caseId:guid}/Persons")]
    [Authorize(Policy = NetraPolicies.CaseManagePersons)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPerson(
        Guid caseId,
        AddPersonToCaseViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["Netra.Error"] = "Select a person and a valid role.";
            return RedirectToAction(nameof(Details), new { id = caseId });
        }

        var request = new AddPersonToCaseRequest
        {
            PersonId = model.PersonId,
            Role = model.Role,
            Notes = model.Notes
        };

        var result = await _cases.AddPersonToCaseAsync(caseId, request, _actor.GetUserId(User), cancellationToken);

        SetOutcome(result.Succeeded, "Person associated with the case.", FirstError(result.Errors) ?? "The person could not be associated.");

        return RedirectToAction(nameof(Details), new { id = caseId });
    }

    [HttpPost("Details/{caseId:guid}/Persons/{casePersonId:guid}/Remove")]
    [Authorize(Policy = NetraPolicies.CaseManagePersons)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemovePerson(
        Guid caseId,
        Guid casePersonId,
        CancellationToken cancellationToken)
    {
        var result = await _cases.RemovePersonFromCaseAsync(
            caseId, casePersonId, _actor.GetUserId(User), cancellationToken);

        SetOutcome(result.Succeeded, "Person removed from the case.", FirstError(result.Errors) ?? "The person could not be removed.");

        return RedirectToAction(nameof(Details), new { id = caseId });
    }

    // ----------------------------------------------- location association

    [HttpPost("Details/{caseId:guid}/Locations")]
    [Authorize(Policy = NetraPolicies.CaseManageLocations)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddLocation(
        Guid caseId,
        AddLocationToCaseViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["Netra.Error"] = "Select a valid location.";
            return RedirectToAction(nameof(Details), new { id = caseId });
        }

        var request = new AddLocationToCaseRequest
        {
            LocationId = model.LocationId,
            LocationRole = model.LocationRole,
            Notes = model.Notes
        };

        var result = await _cases.AddLocationToCaseAsync(caseId, request, _actor.GetUserId(User), cancellationToken);

        SetOutcome(result.Succeeded, "Location associated with the case.", FirstError(result.Errors) ?? "The location could not be associated.");

        return RedirectToAction(nameof(Details), new { id = caseId });
    }

    [HttpPost("Details/{caseId:guid}/Locations/{caseLocationId:guid}/Remove")]
    [Authorize(Policy = NetraPolicies.CaseManageLocations)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveLocation(
        Guid caseId,
        Guid caseLocationId,
        CancellationToken cancellationToken)
    {
        var result = await _cases.RemoveLocationFromCaseAsync(
            caseId, caseLocationId, _actor.GetUserId(User), cancellationToken);

        SetOutcome(result.Succeeded, "Location removed from the case.", FirstError(result.Errors) ?? "The location could not be removed.");

        return RedirectToAction(nameof(Details), new { id = caseId });
    }

    // -------------------------------------------------------- evidence

    [HttpPost("Details/{caseId:guid}/Evidence")]
    [Authorize(Policy = NetraPolicies.CaseManageEvidence)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddEvidence(
        Guid caseId,
        AddEvidenceViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["Netra.Error"] = "Provide an evidence name and a valid evidence type.";
            return RedirectToAction(nameof(Details), new { id = caseId });
        }

        var request = new AddEvidenceRequest
        {
            Title = model.Title,
            Description = model.Description,
            EvidenceType = model.EvidenceType,
            CollectedAt = model.CollectedAt,
            CollectionLocation = model.CollectionLocation,
            ChainOfCustody = model.ChainOfCustody
        };

        var actor = _actor.GetUserId(User);
        var result = await _cases.AddEvidenceAsync(caseId, request, actor, cancellationToken);

        if (!result.Succeeded || result.Value == Guid.Empty)
        {
            TempData["Netra.Error"] = FirstError(result.Errors) ?? "The evidence could not be saved.";
            return RedirectToAction(nameof(Details), new { id = caseId });
        }

        // Optional file attachment.
        if (model.File is { Length: > 0 })
        {
            var fileRequest = new AddEvidenceFileRequest
            {
                OriginalFileName = model.File.FileName,
                ContentType = model.File.ContentType,
                Length = model.File.Length,
                Content = model.File.OpenReadStream()
            };

            var fileResult = await _cases.AttachEvidenceFileAsync(
                caseId, result.Value, fileRequest, actor, cancellationToken);

            if (!fileResult.Succeeded)
            {
                // The evidence record is kept; only the attachment failed.
                TempData["Netra.Error"] = "Evidence saved, but the file was rejected: " +
                                          (FirstError(fileResult.Errors) ?? "invalid file");
                return RedirectToAction(nameof(Details), new { id = caseId });
            }
        }

        TempData["Netra.Success"] = "Evidence recorded.";
        return RedirectToAction(nameof(Details), new { id = caseId });
    }

    // ----------------------------------------------------------- notes

    [HttpPost("Details/{caseId:guid}/Notes")]
    [Authorize(Policy = NetraPolicies.CaseManageNotes)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddNote(
        Guid caseId,
        AddInvestigationNoteViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["Netra.Error"] = "Enter note content.";
            return RedirectToAction(nameof(Details), new { id = caseId });
        }

        var request = new AddInvestigationNoteRequest
        {
            Content = model.Content,
            NoteType = model.NoteType,
            IsPrivate = model.IsPrivate
        };

        var result = await _cases.AddInvestigationNoteAsync(caseId, request, _actor.GetUserId(User), cancellationToken);

        SetOutcome(result.Succeeded, "Investigation note added.", FirstError(result.Errors) ?? "The note could not be saved.");

        return RedirectToAction(nameof(Details), new { id = caseId });
    }

    // -------------------------------------------------------- timeline

    [HttpPost("Details/{caseId:guid}/Timeline")]
    [Authorize(Policy = NetraPolicies.CaseManageTimeline)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddTimelineEvent(
        Guid caseId,
        AddTimelineEventViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["Netra.Error"] = "Provide an event title and a valid date and time.";
            return RedirectToAction(nameof(Details), new { id = caseId });
        }

        var request = new AddTimelineEventRequest
        {
            Title = model.Title,
            Description = model.Description,
            EventDateTime = model.EventDateTime,
            EventType = model.EventType,
            Location = model.Location
        };

        var result = await _cases.AddTimelineEventAsync(caseId, request, _actor.GetUserId(User), cancellationToken);

        SetOutcome(result.Succeeded, "Timeline event added.", FirstError(result.Errors) ?? "The timeline event could not be saved.");

        return RedirectToAction(nameof(Details), new { id = caseId });
    }

    // ------------------------------------------------------- assignment

    [HttpPost("Details/{caseId:guid}/Assign")]
    [Authorize(Policy = NetraPolicies.CaseAssign)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(
        Guid caseId,
        AssignInvestigatorViewModel model,
        CancellationToken cancellationToken)
    {
        var request = new AssignInvestigatorRequest { InvestigatorId = model.InvestigatorId };

        var result = await _cases.AssignInvestigatorAsync(caseId, request, _actor.GetUserId(User), cancellationToken);

        SetOutcome(result.Succeeded, "Investigator assignment updated.", FirstError(result.Errors) ?? "The assignment could not be updated.");

        return RedirectToAction(nameof(Details), new { id = caseId });
    }

    // ---------------------------------------------------------- helpers

    /// <summary>
    /// Writes a flash message. Avoids a conditional inside the TempData indexer, which
    /// is not valid C#.
    /// </summary>
    private void SetOutcome(bool succeeded, string success, string failure)
    {
        if (succeeded)
        {
            TempData["Netra.Success"] = success;
        }
        else
        {
            TempData["Netra.Error"] = failure;
        }
    }

    private static IReadOnlyList<SelectOption> ToOptions(IReadOnlyList<LookupItem> items) =>
        items.Select(i => new SelectOption(i.Id.ToString(), i.Label)).ToList();

    private void ApplyErrors(IReadOnlyDictionary<string, string[]> errors)
    {
        foreach (var (field, messages) in errors)
        {
            foreach (var message in messages)
            {
                ModelState.AddModelError(string.IsNullOrEmpty(field) ? string.Empty : field, message);
            }
        }
    }

    private static string? FirstError(IReadOnlyDictionary<string, string[]> errors) =>
        errors.SelectMany(e => e.Value).FirstOrDefault();
}