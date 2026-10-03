using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NETRA.Application.Abstractions;
using NETRA.Application.CaseOperations;
using NETRA.Application.Reports;
using NETRA.Domain.Enums;
using NETRA.Web.Authorization;
using NETRA.Web.Models.ViewModels.Reports;

namespace NETRA.Web.Controllers;

/// <summary>
/// Reports &amp; intelligence summaries (netra.pdf page 10). The controller only translates
/// requests into <see cref="IReportService"/> calls and operation results into redirect
/// messages; document assembly and every analytical read live in the service layer.
/// </summary>
[Authorize(Policy = NetraPolicies.ReportsRead)]
public class ReportsController : Controller
{
    private readonly IReportService _reports;
    private readonly ICaseService _cases;
    private readonly IAuthorizationService _authorization;
    private readonly IAuditActorAccessor _actor;
    private readonly TimeProvider _time;

    public ReportsController(
        IReportService reports,
        ICaseService cases,
        IAuthorizationService authorization,
        IAuditActorAccessor actor,
        TimeProvider time)
    {
        _reports = reports;
        _cases = cases;
        _authorization = authorization;
        _actor = actor;
        _time = time;
    }

    [HttpGet("/reports")]
    public async Task<IActionResult> Index(
        ReportsFilterViewModel filter, CancellationToken cancellationToken)
    {
        var allowAnalytical = await CanReadAnalyticalAsync();
        var paged = await _reports.ListAsync(filter.ToCriteria(), allowAnalytical, cancellationToken);
        var summary = await _reports.GetSummaryAsync(allowAnalytical, cancellationToken);
        var caseOptions = await _reports.GetCaseOptionsAsync(cancellationToken);

        return View(ReportsIndexViewModel.Create(
            paged, summary, filter, caseOptions, CanManage(), allowAnalytical));
    }

    [HttpGet("/reports/details/{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var document = await _reports.GetDocumentAsync(
            id, _actor.GetUserId(User), await CanReadAnalyticalAsync(), cancellationToken);

        // The service answers null for both a missing report and an analytical report the
        // reader may not open, so unauthorised existence is never confirmed.
        if (document is null)
        {
            return NotFound();
        }

        return View(ReportDocumentViewModel.Create(document, CanManage()));
    }

    /// <summary>The generation form. <c>?type=</c> preselects a report type.</summary>
    [HttpGet("/reports/generate")]
    [Authorize(Policy = NetraPolicies.ReportsManage)]
    public async Task<IActionResult> Generate(
        ReportType? type, CancellationToken cancellationToken)
    {
        var allowAnalytical = await CanReadAnalyticalAsync();
        var selected = type is { } requested
            && ReportTypes.GeneratedOnly.Contains(requested)
            && (allowAnalytical || !ReportTypes.IsAnalytical(requested))
            ? requested
            : ReportType.StatisticalAnalysis;

        return View(await BuildGenerateModelAsync(selected, allowAnalytical, cancellationToken));
    }

    /// <summary>
    /// Explicit, authorised generation pass. Reports are never generated while browsing,
    /// and every fact comes from the existing services — nothing is invented.
    /// </summary>
    [HttpPost("/reports/generate")]
    [Authorize(Policy = NetraPolicies.ReportsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(
        GenerateReportViewModel form, CancellationToken cancellationToken)
    {
        var allowAnalytical = await CanReadAnalyticalAsync();

        if (!ReportTypes.GeneratedOnly.Contains(form.Type))
        {
            ModelState.AddModelError(nameof(form.Type), "Select a report type that can be generated.");
        }
        else if (ReportTypes.IsAnalytical(form.Type) && !allowAnalytical)
        {
            ModelState.AddModelError(nameof(form.Type),
                "You are not authorised to generate analytical reports.");
        }

        if (!ModelState.IsValid)
        {
            return View(await RestoreGenerateModelAsync(form, allowAnalytical, cancellationToken));
        }

        var result = await _reports.GenerateAsync(
            new GenerateReportRequest
            {
                Type = form.Type,
                CaseId = form.CaseId,
                DistrictId = form.DistrictId,
                CrimeTypeId = form.CrimeTypeId,
                Severity = form.Severity,
                DateFrom = form.DateFrom,
                DateTo = form.DateTo,
                AnalystObservations = form.AnalystObservations
            },
            _actor.GetUserId(User), allowAnalytical, cancellationToken);

        if (result.Status == OperationStatus.ValidationFailed)
        {
            CopyErrors(result.Errors, ModelState);
            return View(await RestoreGenerateModelAsync(form, allowAnalytical, cancellationToken));
        }

        if (result.Status != OperationStatus.Success)
        {
            TempData["Netra.Error"] = FirstError(result);
            return RedirectToAction(nameof(Index));
        }

        TempData["Netra.Success"] = "Report generated from the recorded NETRA data.";
        return RedirectToAction(nameof(Details), new { id = result.Value });
    }

    [HttpGet("/reports/create")]
    [Authorize(Policy = NetraPolicies.ReportsManage)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        ViewData["AllowAnalytical"] = await CanReadAnalyticalAsync();
        var caseOptions = await _reports.GetCaseOptionsAsync(cancellationToken);
        return View(AnalystReportFormViewModel.Create(caseOptions));
    }

    [HttpPost("/reports/create")]
    [Authorize(Policy = NetraPolicies.ReportsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        AnalystReportFormViewModel form, CancellationToken cancellationToken)
    {
        ViewData["AllowAnalytical"] = await CanReadAnalyticalAsync();

        if (!ModelState.IsValid)
        {
            form.CaseOptions = await _reports.GetCaseOptionsAsync(cancellationToken);
            return View(form);
        }

        var result = await _reports.CreateAsync(
            new CreateAnalystReportRequest
            {
                Title = form.Title,
                Type = form.Type,
                CaseId = form.CaseId,
                Summary = form.Summary,
                Content = form.Content,
                PeriodFrom = form.PeriodFrom,
                PeriodTo = form.PeriodTo
            },
            _actor.GetUserId(User), cancellationToken);

        if (result.Status == OperationStatus.ValidationFailed)
        {
            CopyErrors(result.Errors, ModelState);
            form.CaseOptions = await _reports.GetCaseOptionsAsync(cancellationToken);
            return View(form);
        }

        if (result.Status != OperationStatus.Success)
        {
            TempData["Netra.Error"] = FirstError(result);
            return RedirectToAction(nameof(Index));
        }

        TempData["Netra.Success"] = "Report saved.";
        return RedirectToAction(nameof(Details), new { id = result.Value });
    }

    [HttpGet("/reports/edit/{id:guid}")]
    [Authorize(Policy = NetraPolicies.ReportsManage)]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        ViewData["AllowAnalytical"] = await CanReadAnalyticalAsync();
        var document = await _reports.GetDocumentAsync(
            id, _actor.GetUserId(User), await CanReadAnalyticalAsync(), cancellationToken);

        if (document is null)
        {
            return NotFound();
        }

        if (document.Source != ReportSource.AnalystAuthored)
        {
            TempData["Netra.Error"] =
                "System-generated facts are immutable. Record your observations when the report is generated.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (document.Status == ReportStatus.Archived)
        {
            TempData["Netra.Error"] = "Archived reports cannot be edited.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var caseOptions = await _reports.GetCaseOptionsAsync(cancellationToken);
        return View(AnalystReportFormViewModel.FromDocument(document, caseOptions));
    }

    [HttpPost("/reports/edit/{id:guid}")]
    [Authorize(Policy = NetraPolicies.ReportsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        Guid id, AnalystReportFormViewModel form, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            form.Id = id;
            form.CaseOptions = await _reports.GetCaseOptionsAsync(cancellationToken);
            return View(form);
        }

        var result = await _reports.EditAsync(
            id,
            new EditAnalystReportRequest
            {
                Title = form.Title,
                Summary = form.Summary,
                Content = form.Content,
                PeriodFrom = form.PeriodFrom,
                PeriodTo = form.PeriodTo
            },
            _actor.GetUserId(User), cancellationToken);

        if (result.Status == OperationStatus.ValidationFailed)
        {
            CopyErrors(result.Errors, ModelState);
            form.Id = id;
            form.CaseOptions = await _reports.GetCaseOptionsAsync(cancellationToken);
            return View(form);
        }

        TempData[result.Succeeded ? "Netra.Success" : "Netra.Error"] = result.Succeeded
            ? "Report updated."
            : FirstError(result);

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("/reports/archive/{id:guid}")]
    [Authorize(Policy = NetraPolicies.ReportsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Archive(Guid id, CancellationToken cancellationToken)
    {
        var result = await _reports.ArchiveAsync(id, _actor.GetUserId(User), cancellationToken);

        TempData[result.Succeeded ? "Netra.Success" : "Netra.Error"] = result.Succeeded
            ? "Report archived. It remains available for reading."
            : FirstError(result);

        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>Server-rendered CSV of the same document the page shows. No client-side library.</summary>
    [HttpGet("/reports/export/{id:guid}")]
    public async Task<IActionResult> Export(Guid id, CancellationToken cancellationToken)
    {
        var export = await _reports.ExportCsvAsync(
            id, _actor.GetUserId(User), await CanReadAnalyticalAsync(), cancellationToken);

        if (export is null)
        {
            return NotFound();
        }

        return File(export.Value.Content, "text/csv; charset=utf-8", export.Value.FileName);
    }

    private async Task<GenerateReportViewModel> BuildGenerateModelAsync(
        ReportType selected, bool allowAnalytical, CancellationToken cancellationToken)
    {
        var today = _time.GetUtcNow().UtcDateTime.Date;

        return await RestoreGenerateModelAsync(new GenerateReportViewModel
        {
            Type = selected,
            DefaultDateFrom = today.AddDays(-(ReportLimits.DefaultPeriodDays - 1)),
            DefaultDateTo = today
        }, allowAnalytical, cancellationToken);
    }

    /// <summary>Refills the dropdowns for a submitted form without touching the user's values.</summary>
    private async Task<GenerateReportViewModel> RestoreGenerateModelAsync(
        GenerateReportViewModel form, bool allowAnalytical, CancellationToken cancellationToken)
    {
        var lookups = await _cases.GetLookupsAsync(cancellationToken);
        var today = _time.GetUtcNow().UtcDateTime.Date;

        form.TypeOptions = ReportTypes.GeneratedOnly
            .Where(type => allowAnalytical || !ReportTypes.IsAnalytical(type))
            .Select(type => (type, ReportTypes.Label(type), ReportTypes.Description(type)))
            .ToList();
        form.CaseOptions = await _reports.GetCaseOptionsAsync(cancellationToken);
        form.Districts = lookups.Districts;
        form.CrimeTypes = lookups.CrimeTypes;

        if (form.DefaultDateTo == default)
        {
            form.DefaultDateFrom = today.AddDays(-(ReportLimits.DefaultPeriodDays - 1));
            form.DefaultDateTo = today;
        }

        return form;
    }

    private static void CopyErrors(
        IReadOnlyDictionary<string, string[]> errors, ModelStateDictionary modelState)
    {
        foreach (var (field, messages) in errors)
        {
            foreach (var message in messages)
            {
                modelState.AddModelError(field, message);
            }
        }
    }

    /// <summary>
    /// Evaluates the analytical policy instead of re-listing role names, so the report
    /// content gate and the policy registration cannot drift apart.
    /// </summary>
    private async Task<bool> CanReadAnalyticalAsync() =>
        (await _authorization.AuthorizeAsync(User, null, NetraPolicies.ReportsAnalysis)).Succeeded;

    private bool CanManage() =>
        User.IsInRole(NetraRoles.SystemAdministrator)
        || User.IsInRole(NetraRoles.SeniorOfficer)
        || User.IsInRole(NetraRoles.PoliceIntelligenceAnalyst);

    private static string FirstError(OperationResult result) =>
        result.Errors.Values.SelectMany(e => e).FirstOrDefault() ?? "The report could not be updated.";

    private static string FirstError<T>(OperationResult<T> result) =>
        result.Errors.Values.SelectMany(e => e).FirstOrDefault() ?? "The report could not be updated.";
}
