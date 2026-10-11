using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NETRA.Application.Abstractions;
using NETRA.Application.BulkImport;
using NETRA.Web.Authorization;
using NETRA.Web.Models.ViewModels.BulkImport;

namespace NETRA.Web.Controllers;

/// <summary>
/// Bulk Data Import: the UPLOAD, VALIDATE, PREVIEW, IMPORT, RESULT workflow for crime-case
/// CSV files.
/// </summary>
/// <remarks>
/// The controller is deliberately thin. It binds the upload form, passes the acting user to
/// <see cref="IBulkImportService"/>, and renders the stage the service reports. It contains no
/// CSV parsing, no database access and no import rules, so the workflow cannot be driven out
/// of order from the browser: each action asks the service whether the job is in a state that
/// allows it, and the service refuses otherwise.
///
/// Authorisation is enforced here by policy, not by hiding menu items. Ownership of a job is
/// enforced inside the service, so one operator cannot continue another's import.
/// </remarks>
[Authorize(Policy = NetraPolicies.BulkImportManage)]
public sealed class BulkImportController : Controller
{
    private readonly IBulkImportService _imports;
    private readonly IAuditActorAccessor _actor;

    public BulkImportController(IBulkImportService imports, IAuditActorAccessor actor)
    {
        _imports = imports;
        _actor = actor;
    }

    /// <summary>Module landing page: the contract, the limits and this operator's recent imports.</summary>
    [HttpGet("/BulkImport")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var requirements = await _imports.GetRequirementsAsync(cancellationToken);
        var recent = await _imports.GetRecentJobsAsync(_actor.GetUserId(User), cancellationToken);

        return View(new BulkImportIndexViewModel
        {
            Requirements = requirements,
            RecentJobs = recent
        });
    }

    [HttpGet("/BulkImport/Upload")]
    public async Task<IActionResult> Upload(CancellationToken cancellationToken)
    {
        var requirements = await _imports.GetRequirementsAsync(cancellationToken);

        return View(new BulkImportUploadViewModel { Requirements = requirements });
    }

    /// <summary>
    /// Stores the file and registers a pending job. Nothing is parsed or staged here: the
    /// operator reviews the file on the validation page that follows.
    /// </summary>
    [HttpPost("/BulkImport/Upload")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(
        BulkImportUploadForm form,
        CancellationToken cancellationToken)
    {
        var requirements = await _imports.GetRequirementsAsync(cancellationToken);

        if (form.File is null || form.File.Length == 0)
        {
            ModelState.AddModelError(nameof(BulkImportUploadForm.File), "Choose a CSV file to upload.");
        }

        if (!ModelState.IsValid)
        {
            return View(ViewName, new BulkImportUploadViewModel
            {
                Requirements = requirements,
                Form = form
            });
        }

        await using var stream = form.File!.OpenReadStream();

        var result = await _imports.UploadAsync(new UploadBulkImportRequest
        {
            Name = form.Name,
            Description = form.Description,
            OriginalFileName = form.File.FileName,
            ContentType = form.File.ContentType,
            Length = form.File.Length,
            Content = stream
        }, _actor.GetUserId(User), cancellationToken);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                // The service names the upload field "OriginalFileName"; the form calls it File.
                var field = error.Key.Length == 0 || error.Key == nameof(UploadBulkImportRequest.OriginalFileName)
                    ? nameof(BulkImportUploadForm.File)
                    : error.Key;

                foreach (var message in error.Value)
                {
                    ModelState.AddModelError(field, message);
                }
            }

            return View(ViewName, new BulkImportUploadViewModel
            {
                Requirements = requirements,
                Form = form
            });
        }

        return RedirectToAction(nameof(Validate), new { id = result.Value!.Id });
    }

    /// <summary>
    /// Displays the validation outcome for a job. This GET endpoint is strictly read-only:
    /// it parses nothing, stages no rows, and performs zero database mutations.
    /// </summary>
    [HttpGet("/BulkImport/Validate/{id:guid}")]
    public async Task<IActionResult> Validate(Guid id, CancellationToken cancellationToken)
    {
        var result = await _imports.GetValidationAsync(id, _actor.GetUserId(User), cancellationToken);

        if (!result.Succeeded)
        {
            TempData["Netra.Error"] = FirstMessage(result);
            return RedirectToAction(nameof(Index));
        }

        var validation = result.Value!;

        return View(new BulkImportValidateViewModel
        {
            Validation = validation,
            ShownIssues = validation.Issues,
            ShowImportButton = validation.Job.CanImport
        });
    }

    /// <summary>
    /// Runs or re-runs validation for a job and redirects to the display page.
    /// State mutations occur strictly within this POST endpoint with anti-forgery validation.
    /// </summary>
    [HttpPost("/BulkImport/Validate/{id:guid}")]
    [ActionName("Validate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ValidatePost(Guid id, CancellationToken cancellationToken)
    {
        var result = await _imports.ValidateAsync(id, _actor.GetUserId(User), cancellationToken);

        if (!result.Succeeded)
        {
            TempData["Netra.Error"] = FirstMessage(result);
            return RedirectToAction(nameof(Index));
        }

        return RedirectToAction(nameof(Validate), new { id });
    }

    /// <summary>Read-only look at the rows an import would accept. Imports nothing.</summary>
    [HttpGet("/BulkImport/Preview/{id:guid}")]
    public async Task<IActionResult> Preview(Guid id, CancellationToken cancellationToken)
    {
        var result = await _imports.PreviewAsync(id, _actor.GetUserId(User), cancellationToken);

        if (!result.Succeeded)
        {
            TempData["Netra.Error"] = FirstMessage(result);
            return RedirectToAction(nameof(Index));
        }

        return View(new BulkImportPreviewViewModel { Preview = result.Value! });
    }

    /// <summary>
    /// Confirms the import. Only ever reached by a POST from the preview page, and the service
    /// re-checks the job's state before it writes anything.
    /// </summary>
    [HttpPost("/BulkImport/Import/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(Guid id, CancellationToken cancellationToken)
    {
        var result = await _imports.ImportAsync(id, _actor.GetUserId(User), cancellationToken);

        if (!result.Succeeded)
        {
            TempData["Netra.Error"] = FirstMessage(result);
            return RedirectToAction(nameof(Preview), new { id });
        }

        return RedirectToAction(nameof(Result), new { id });
    }

    /// <summary>Abandons a staged job without importing it.</summary>
    [HttpPost("/BulkImport/Cancel/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var result = await _imports.CancelAsync(id, _actor.GetUserId(User), cancellationToken);

        if (!result.Succeeded)
        {
            TempData["Netra.Error"] = FirstMessage(result);
        }

        return RedirectToAction(nameof(Index));
    }

    /// <summary>Outcome of a finished, failed or cancelled import.</summary>
    [HttpGet("/BulkImport/Result/{id:guid}")]
    public async Task<IActionResult> Result(Guid id, CancellationToken cancellationToken)
    {
        var result = await _imports.GetResultAsync(id, _actor.GetUserId(User), cancellationToken);

        if (!result.Succeeded)
        {
            TempData["Netra.Error"] = FirstMessage(result);
            return RedirectToAction(nameof(Index));
        }

        return View(new BulkImportResultViewModel { Result = result.Value! });
    }

    private const string ViewName = nameof(Upload);

    /// <summary>
    /// Surfaces one honest reason for a refusal. The service already distinguishes "not found"
    /// from "not yours" from "wrong stage", so no extra branching belongs here.
    /// </summary>
    private static string FirstMessage<T>(OperationResult<T> result) =>
        result.Errors.Values.SelectMany(messages => messages).FirstOrDefault()
        ?? "The import could not continue.";
}
