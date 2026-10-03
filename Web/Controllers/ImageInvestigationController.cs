using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NETRA.Application.Abstractions;
using NETRA.Application.ImageInvestigation;
using NETRA.Web.Authorization;
using NETRA.Web.Models.ViewModels.ImageInvestigation;

namespace NETRA.Web.Controllers;

/// <summary>
/// Image-Assisted Investigation: upload an investigation image and review
/// deterministic, synthetic candidate leads from NETRA's own stored images.
/// The controller only maps form input to the service request and renders the
/// result; all validation, storage and matching live behind
/// <see cref="IImageInvestigationService"/>.
/// </summary>
[Authorize(Policy = NetraPolicies.ImageInvestigation)]
public sealed class ImageInvestigationController : Controller
{
    private readonly IImageInvestigationService _images;
    private readonly IAuditActorAccessor _actor;

    public ImageInvestigationController(IImageInvestigationService images, IAuditActorAccessor actor)
    {
        _images = images;
        _actor = actor;
    }

    [HttpGet("/image-investigation")]
    public async Task<IActionResult> Index(Guid? record, CancellationToken cancellationToken)
    {
        var cases = await _images.GetSelectableCasesAsync(cancellationToken);

        if (record is null)
        {
            return View(ImageInvestigationViewModel.Create(cases, result: null));
        }

        var result = await _images.GetResultAsync(
            record.Value, _actor.GetUserId(User), cancellationToken);

        if (result is null)
        {
            TempData["Netra.Error"] = "That image investigation was not found. Upload an image to start a new one.";
            return View(ImageInvestigationViewModel.Create(cases, result: null));
        }

        return View(ImageInvestigationViewModel.Create(cases, result));
    }

    [HttpPost("/image-investigation/Analyze")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Analyze(
        // The form binds against ImageInvestigationViewModel, so its inputs are
        // named "Upload.*"; this action must read them under the same prefix.
        [Bind(Prefix = "Upload")] ImageUploadViewModel model,
        CancellationToken cancellationToken)
    {
        var cases = await _images.GetSelectableCasesAsync(cancellationToken);
        var caseId = model.CrimeCaseId ?? Guid.Empty;

        if (model.File is null || model.File.Length == 0)
        {
            ModelState.AddModelError("Upload." + nameof(ImageUploadViewModel.File), "Choose an image file to upload.");
        }

        if (caseId == Guid.Empty)
        {
            ModelState.AddModelError("Upload." + nameof(ImageUploadViewModel.CrimeCaseId), "Select the case this image belongs to.");
        }

        if (!ModelState.IsValid)
        {
            return View(nameof(Index), ImageInvestigationViewModel.Create(cases, result: null, model.CrimeCaseId));
        }

        await using var stream = model.File!.OpenReadStream();

        var result = await _images.AnalyzeAsync(new AnalyzeImageRequest
        {
            CrimeCaseId = caseId,
            OriginalFileName = model.File.FileName,
            ContentType = model.File.ContentType,
            Length = model.File.Length,
            Content = stream
        }, _actor.GetUserId(User), cancellationToken);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                // Map service validation keys onto the form fields they describe so
                // the field-level spans in the view render them.
                var target = error.Key switch
                {
                    "File" => nameof(ImageUploadViewModel.File),
                    "CrimeCaseId" => nameof(ImageUploadViewModel.CrimeCaseId),
                    _ => error.Key
                };

                foreach (var message in error.Value)
                {
                    ModelState.AddModelError(
                        target.Length == 0 ? string.Empty : $"Upload.{target}", message);
                }
            }

            return View(nameof(Index), ImageInvestigationViewModel.Create(cases, result: null, caseId));
        }

        return RedirectToAction(nameof(Index), new { record = result.Value!.ImageRecordId });
    }

    [HttpGet("/image-investigation/Match/{match:guid}")]
    public async Task<IActionResult> Match(Guid match, CancellationToken cancellationToken)
    {
        var detail = await _images.GetMatchDetailAsync(
            match, _actor.GetUserId(User), cancellationToken);

        if (detail is null)
        {
            return NotFound();
        }

        return View(new ImageMatchDetailsViewModel { Detail = detail });
    }

    /// <summary>
    /// Authorised inline preview of a stored investigation image. Resolves the
    /// bytes through the storage abstraction by record id only — no user-supplied
    /// paths and no direct file serving.
    /// </summary>
    [HttpGet("/image-investigation/Image/{id:guid}")]
    public async Task<IActionResult> Image(Guid id, CancellationToken cancellationToken)
    {
        var content = await _images.OpenImageAsync(id, cancellationToken);

        if (content is null)
        {
            return NotFound();
        }

        return File(content.Content, content.ContentType, enableRangeProcessing: false);
    }
}
